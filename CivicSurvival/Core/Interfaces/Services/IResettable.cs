namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Interface for classes that can reset their state to defaults.
    /// Used in conjunction with SerializationGuard for unified save/load pattern.
    ///
    /// Implementing classes should:
    /// 1. Reset ALL serializable fields to their initial values
    /// 2. Call ResetState() from SetDefaults(Context) for IDefaultSerializable compliance
    ///
    /// Example:
    /// <code>
    /// public class MySystem : GameSystemBase, IDefaultSerializable, IResettable
    /// {
    ///     public void SetDefaults(Context context) => ResetState();
    ///
    ///     public void ResetState()
    ///     {
    ///         m_Counter = 0;
    ///         m_IsActive = false;
    ///         m_Data.Clear();
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface IResettable
    {
        /// <summary>
        /// Reset all state to default values.
        /// Called on new game and when save version is incompatible.
        /// </summary>
        void ResetState();
    }
}
