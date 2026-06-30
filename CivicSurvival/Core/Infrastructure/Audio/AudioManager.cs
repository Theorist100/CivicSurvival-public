using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Colossal.Logging;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Networking;

namespace CivicSurvival.Core.Infrastructure.Audio
{
    /// <summary>
    /// Generic audio loading and playback manager for Civic Survival mod.
    /// Provides infrastructure utilities for loading audio files from mod's audio folder
    /// and creating/managing AudioSources.
    ///
    /// This is pure infrastructure - no domain-specific logic.
    /// Domains (Threats, BackupPower, etc.) use this to load their own audio clips.
    ///
    /// Features:
    /// - Async audio loading from mod folder (no blocking)
    /// - 2D and 3D spatial audio source creation
    /// - One-shot positional playback
    /// - AudioClip caching
    ///
    /// THREAD SAFETY: Main thread only.
    /// - All Unity API calls (coroutines, AudioSource) must be on main thread
    /// - LoadClipAsync uses coroutines which run on main thread
    /// - Do NOT call from Jobs or background threads
    ///
    /// Access via ServiceRegistry.Instance.Get&lt;AudioManager&gt;()
    /// </summary>
    [InfrastructureService]
    public class AudioManager : MonoBehaviour
    {
        private static readonly LogContext Log = new("AudioManager");

        /// <summary>
        /// Create AudioManager instance. Call from Mod.OnLoad().
        /// </summary>
        public static AudioManager Create()
        {
            var go = new GameObject("CivicSurvival_AudioManager");
            var instance = go.AddComponent<AudioManager>();
            DontDestroyOnLoad(go);
            return instance;
        }

        // Audio clips cache (lazy loaded on demand)
        private readonly Dictionary<string, AudioClip> m_Clips = new();

        // Track created audio sources for explicit cleanup
        private readonly List<AudioSource> m_CreatedSources = new();

        // FIX H102: Track in-flight loads to prevent duplicate coroutines and orphaned AudioClips
        private readonly HashSet<string> m_InFlightLoads = new();
        // FIX H103: Track pending callbacks so Cleanup can invoke them with null
        private readonly Dictionary<string, List<Action<AudioClip?>>> m_PendingCallbacks = new();

        // State
        private bool m_Initialized;
        private string m_AudioPath = string.Empty;
        private int m_MainThreadId;

        public bool IsInitialized => m_Initialized;

        private void Awake()
        {
            m_MainThreadId = System.Environment.CurrentManagedThreadId;
        }

        private bool EnsureMainThread(string operation)
        {
            if (m_MainThreadId == 0)
                m_MainThreadId = System.Environment.CurrentManagedThreadId;

            if (System.Environment.CurrentManagedThreadId == m_MainThreadId)
                return true;

            Log.Error($"[AudioManager] {operation} called from non-main thread");
            return false;
        }

        /// <summary>
        /// Initialize audio system. Call from Mod.OnLoad or system OnCreate.
        /// </summary>
        public void Initialize()
        {
            if (!EnsureMainThread(nameof(Initialize)))
                throw new InvalidOperationException("[AudioManager] Initialize must run on the main thread");

            if (m_Initialized) return;

            // Find audio folder path - use multiple fallback methods.
            // Missing audio assets are non-fatal; path resolution failures are boot bugs.
            string? dllPath = null;

            // Method 1: Assembly location (may be empty in some contexts)
            try
            {
                var location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location))
                {
                    dllPath = Path.GetDirectoryName(location);
                }
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($"[AudioManager] Assembly.Location failed, using fallback path: {ex.Message}");
            }

            // Method 2: Known CS2 mods path
            if (string.IsNullOrEmpty(dllPath))
            {
                dllPath = ModPaths.ModInstallDirectory;
            }

            m_AudioPath = Path.Combine(dllPath, "audio");

            Log.Info($"[AudioManager] Audio path: {m_AudioPath}");

            if (!Directory.Exists(m_AudioPath))
            {
                Log.Warn($"[AudioManager] Audio folder not found: {m_AudioPath}");
                m_Initialized = true; // Mark as initialized anyway to avoid repeated checks
                return;
            }

            m_Initialized = true;
            Log.Info("[AudioManager] Initialized (generic audio infrastructure)");
        }

        // ============================================================================
        // Public API - Generic Audio Operations
        // ============================================================================

        /// <summary>
        /// Load audio clip asynchronously from mod's audio folder.
        /// Path is relative to mod's audio folder (e.g., "drone_buzz.ogg").
        /// </summary>
        /// <param name="relativePath">Relative path within audio folder</param>
        /// <param name="onLoaded">Callback when clip is loaded (or failed)</param>
        public void LoadClipAsync(string relativePath, Action<AudioClip?> onLoaded)
        {
            if (!EnsureMainThread(nameof(LoadClipAsync)))
            {
                onLoaded?.Invoke(null);
                return;
            }

            if (!m_Initialized)
            {
                Log.Warn($"[AudioManager] Cannot load clip - not initialized: {relativePath}");
                onLoaded?.Invoke(null);
                return;
            }

            // Check cache first.
            m_Clips.TryGetValue(relativePath, out var cachedClip);
            if (cachedClip != null)
            {
                if (Log.IsDebugEnabled) Log.Debug($"[AudioManager] Clip already cached: {relativePath}");
                onLoaded?.Invoke(cachedClip);
                return;
            }

            // FIX H102: If already loading this clip, queue callback instead of starting duplicate coroutine
            if (m_InFlightLoads.Contains(relativePath))
            {
                if (m_PendingCallbacks.TryGetValue(relativePath, out var callbacks))
                    callbacks.Add(onLoaded);
                return;
            }

            m_InFlightLoads.Add(relativePath);
            m_PendingCallbacks[relativePath] = new List<Action<AudioClip?>> { onLoaded };

            // Load async
            StartCoroutine(LoadClipCoroutine(relativePath));
        }

        /// <summary>
        /// Create AudioSource with 2D or 3D spatial settings.
        /// Returns AudioSource attached to child GameObject.
        /// Call DestroyAudioSource() when no longer needed to prevent accumulation.
        /// </summary>
        /// <param name="name">Name for GameObject</param>
        /// <param name="loop">Should audio loop</param>
        /// <param name="volume">Initial volume (0-1)</param>
        /// <param name="spatial3D">True for 3D spatial audio, false for 2D</param>
        /// <param name="minDistance">3D min distance (full volume)</param>
        /// <param name="maxDistance">3D max distance (silence)</param>
        public AudioSource? CreateAudioSource(
            string name,
            bool loop = false,
            float volume = 1f,
            bool spatial3D = false,
            float minDistance = Engine.Audio.DEFAULT_MIN_DISTANCE,
            float maxDistance = Engine.Audio.DEFAULT_MAX_DISTANCE)
        {
            if (!EnsureMainThread(nameof(CreateAudioSource)))
                return null;

            var go = new GameObject($"SC_Audio_{name}");
            go.transform.SetParent(transform);

            var source = go.AddComponent<AudioSource>();
            source.loop = loop;
            source.volume = math.clamp(volume, 0f, 1f);
            source.playOnAwake = false;

            // Spatial settings
            source.spatialBlend = spatial3D ? 1.0f : 0f;  // 1.0 = full 3D, 0 = 2D
            source.dopplerLevel = spatial3D ? Engine.Audio.SPATIAL_DOPPLER_LEVEL : 0f;

            if (spatial3D)
            {
                source.minDistance = minDistance;
                source.maxDistance = maxDistance;
                source.rolloffMode = AudioRolloffMode.Linear;
            }

            // Track for cleanup
            m_CreatedSources.Add(source);

            return source;
        }

        /// <summary>
        /// Destroy a specific AudioSource created by CreateAudioSource().
        /// Call when audio source is no longer needed to prevent accumulation.
        /// </summary>
        public void DestroyAudioSource(AudioSource source)
        {
            if (!EnsureMainThread(nameof(DestroyAudioSource)))
                return;

            if (source == null) return;

            m_CreatedSources.Remove(source);

            if (source.gameObject != null)
            {
                Destroy(source.gameObject);
            }
        }

        /// <summary>
        /// Number of active audio sources created by this manager.
        /// </summary>
        public int ActiveSourceCount => m_CreatedSources.Count;

        /// <summary>
        /// Play one-shot audio clip at world position.
        /// Uses Unity's PlayClipAtPoint for 3D positional audio.
        /// </summary>
        public void PlayOneShot3D(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null)
            {
                Log.Warn("[AudioManager] Cannot play null clip");
                return;
            }

            AudioSource.PlayClipAtPoint(clip, position, math.clamp(volume, 0f, 1f));
        }

        /// <summary>
        /// Get cached clip by relative path (returns null if not loaded).
        /// Use LoadClipAsync() to load first.
        /// </summary>
        public AudioClip? GetClip(string relativePath)
        {
            if (!EnsureMainThread(nameof(GetClip)))
                return null;

            m_Clips.TryGetValue(relativePath, out var clip);
            return clip;
        }

        // ============================================================================
        // Internal Implementation
        // ============================================================================

        private IEnumerator LoadClipCoroutine(string relativePath)
        {
            string fullPath = Path.Combine(m_AudioPath, relativePath);

            if (!File.Exists(fullPath))
            {
                Log.Warn($"[AudioManager] Audio file not found: {fullPath}");
                InvokeAndClearPendingCallbacks(relativePath, null);
                yield break;
            }

            AudioType audioType = GetAudioType(relativePath);
            var uri = new Uri(fullPath).AbsoluteUri;

            // FMOD cannot seek inside a long compressed OGG held in memory and aborts
            // clip creation ("Couldn't perform seek operation"), which crashed load.
            // Stream clips above this size from disk so FMOD reads sequentially instead
            // of seeking the compressed blob; short SFX stay in-memory so they keep
            // playing across many concurrent AudioSource instances.
            const long StreamFromDiskThresholdBytes = 512 * 1024;
            bool streamFromDisk = new FileInfo(fullPath).Length > StreamFromDiskThresholdBytes;

#pragma warning disable CA2234 // Pass System.Uri objects instead of strings - Unity API requires string
            using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
#pragma warning restore CA2234
            {
                if (request.downloadHandler is DownloadHandlerAudioClip audioHandler)
                    audioHandler.streamAudio = streamFromDisk;

                // Async send - yields control back to Unity
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip != null)
                    {
                        clip.name = Path.GetFileNameWithoutExtension(relativePath);
                        m_Clips[relativePath] = clip;
                        Log.Info($"[AudioManager] Loaded: {relativePath} ({clip.length:F1}s)");
                        InvokeAndClearPendingCallbacks(relativePath, clip);
                    }
                    else
                    {
                        Log.Warn($"[AudioManager] Failed to extract clip from: {relativePath}");
                        InvokeAndClearPendingCallbacks(relativePath, null);
                    }
                }
                else
                {
                    Log.Warn($"[AudioManager] Failed to load {relativePath}: {request.error}");
                    InvokeAndClearPendingCallbacks(relativePath, null);
                }
            }
        }

        /// <summary>
        /// FIX H102+H103: Invoke all queued callbacks for a clip and clean up in-flight tracking.
        /// </summary>
        private void InvokeAndClearPendingCallbacks(string relativePath, AudioClip? clip)
        {
            m_InFlightLoads.Remove(relativePath);
            if (m_PendingCallbacks.TryGetValue(relativePath, out var callbacks))
            {
                m_PendingCallbacks.Remove(relativePath);
                foreach (var cb in callbacks)
                    cb?.Invoke(clip);
            }
        }

        private AudioType GetAudioType(string filename)
        {
            string ext = Path.GetExtension(filename).ToUpperInvariant();
#pragma warning disable CIVIC135 // File extension → AudioType: string input by design
            return ext switch
            {
                ".OGG" => AudioType.OGGVORBIS,
                ".MP3" => AudioType.MPEG,
                ".WAV" => AudioType.WAV,
                _ => AudioType.UNKNOWN
            };
        }

        /// <summary>
        /// Cleanup audio resources. Called from Mod.OnDispose().
        /// NOTE: Named Cleanup() instead of Dispose() because MonoBehaviour
        /// uses Unity lifecycle (OnDestroy), not IDisposable pattern.
        /// </summary>
        public void Cleanup()
        {
            if (!EnsureMainThread(nameof(Cleanup)))
                return;

            // FIX H103: Invoke pending callbacks with null BEFORE killing coroutines.
            // Without this, callers waiting for onLoaded are permanently blocked.
            foreach (var kvp in m_PendingCallbacks)
            {
                foreach (var cb in kvp.Value)
                    cb?.Invoke(null);
            }
            m_PendingCallbacks.Clear();
            m_InFlightLoads.Clear();

            StopAllCoroutines();

            // Stop all child AudioSources
            foreach (var source in GetComponentsInChildren<AudioSource>())
            {
                if (source.isPlaying)
                {
                    source.Stop();
                }
            }

            m_Clips.Clear();
            for (int i = m_CreatedSources.Count - 1; i >= 0; i--)
            {
                var source = m_CreatedSources[i];
                if (source != null && source.gameObject != null)
                    Destroy(source.gameObject);
            }
            m_CreatedSources.Clear();
            m_Initialized = false;

            // FIX S26_RAG1:F52: Log before Destroy to avoid MissingReferenceException
            Log.Info("[AudioManager] Cleaned up");

            if (gameObject != null)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            m_Clips.Clear();
            m_CreatedSources.Clear();
        }
    }
}
