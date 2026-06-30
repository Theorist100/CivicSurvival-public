using System;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.CameraTracking;
using Unity.Entities;

namespace CivicSurvival.Services.Bootstrap
{
    /// <summary>
    /// Registers process-lifetime facades and reconnects them to live world hosts
    /// after mod hot-reload.
    /// </summary>
    internal static class ProcessLifetimeFacadeBootstrapper
    {
        public static void Register(ServiceRegistry services)
        {
            services.Register(new NameSystemFacade());
            services.Register(new CameraFocusState());
            services.Register(new CameraTrackingState());
            services.Register(new ClimateState());

            var mapBounds = new MapBoundsState();
            services.Register<MapBoundsState>(mapBounds);
            services.Register<IMapBoundsReader>(mapBounds);
            services.Register<ITerrainHeightReader>(mapBounds);

            var mapContour = new MapContourState();
            services.Register<MapContourState>(mapContour);
            services.Register<IMapContourReader>(mapContour);

            var planetaryClock = new PlanetaryClockState();
            services.Register<PlanetaryClockState>(planetaryClock);
            services.Register<IPlanetaryClockReader>(planetaryClock);

            var lighting = new LightingPhaseState();
            services.Register<LightingPhaseState>(lighting);
            services.Register<ILightingPhaseReader>(lighting);

            var areas = new AreaCollectState();
            services.Register<AreaCollectState>(areas);
            services.Register<IAreaCollectReader>(areas);

            services.Register(new CityBudgetFacade());
            services.Register(new EntityCountProbeFacade());
            services.Register(new PopulationCountFacade());
        }

        public static void Unregister(ServiceRegistry services)
        {
            services.Unregister<NameSystemFacade>();
            services.Unregister<CameraFocusState>();
            services.Unregister<CameraTrackingState>();
            services.Unregister<ClimateState>();
            services.Unregister<IPlanetaryClockReader>();
            services.Unregister<PlanetaryClockState>();
            services.Unregister<ITerrainHeightReader>();
            services.Unregister<IMapBoundsReader>();
            services.Unregister<MapBoundsState>();
            services.Unregister<IMapContourReader>();
            services.Unregister<MapContourState>();
            services.Unregister<ILightingPhaseReader>();
            services.Unregister<LightingPhaseState>();
            services.Unregister<IAreaCollectReader>();
            services.Unregister<AreaCollectState>();
            services.Unregister<CityBudgetFacade>();
            services.Unregister<EntityCountProbeFacade>();
            services.Unregister<PopulationCountFacade>();
        }

        public static void ReattachToLiveHosts(ServiceRegistry services, World? world)
        {
            if (world == null || !world.IsCreated) return;

            ReattachFacade<NameSystemFacade, NameSystemHost>(
                services,
                world,
                (facade, host) => facade.CurrentHost = host);

            ReattachFacade<CityBudgetFacade, CityBudgetHost>(
                services,
                world,
                (facade, host) => facade.CurrentHost = host);

            ReattachFacade<EntityCountProbeFacade, EntityCountProbeHost>(
                services,
                world,
                (facade, host) => facade.CurrentHost = host);

            ReattachFacade<PopulationCountFacade, PopulationCountHost>(
                services,
                world,
                (facade, host) => facade.CurrentHost = host);

            ReattachFacade<ClimateState, VanillaClimateAdapter>(
                services,
                world,
                (facade, host) => host.BindFacade(facade));

            ReattachFacade<MapBoundsState, VanillaTerrainAdapter>(
                services,
                world,
                (facade, host) => host.BindFacade(facade));

            ReattachFacade<MapContourState, VanillaMapContourAdapter>(
                services,
                world,
                (facade, host) => host.BindFacade(facade));

            ReattachFacade<PlanetaryClockState, VanillaPlanetaryClockAdapter>(
                services,
                world,
                (facade, host) => host.BindFacade(facade));

            ReattachFacade<LightingPhaseState, VanillaLightingAdapter>(
                services,
                world,
                (facade, host) => host.BindFacade(facade));

            ReattachFacade<AreaCollectState, VanillaAreasAdapter>(
                services,
                world,
                (facade, host) => host.BindFacade(facade));

            ReattachFacade<CameraTrackingState, CameraTrackingSystem>(
                services,
                world,
                (facade, host) => host.BindFacade(facade));
        }

        private static void ReattachFacade<TFacade, THost>(
            ServiceRegistry services,
            World world,
            Action<TFacade, THost> bind)
            where TFacade : class
            where THost : ComponentSystemBase
        {
            var facade = services.Get<TFacade>();
            if (facade == null)
            {
                Mod.Log.Error($"[Bootstrap] Facade {typeof(TFacade).Name} not registered in ServiceRegistry — host {typeof(THost).Name} will be unbound");
                return;
            }

            var host = world.GetExistingSystemManaged<THost>();
            if (host == null)
            {
                Mod.Log.Error($"[Bootstrap] Host {typeof(THost).Name} not created in world — facade {typeof(TFacade).Name} will be unbound (no snapshots will reach readers)");
                return;
            }

            bind(facade, host);
        }
    }
}
