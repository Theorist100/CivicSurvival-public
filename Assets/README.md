# Assets

All assets in this folder are licensed under **CC BY-NC-ND 4.0**.
See [LICENSE](./LICENSE) for details.

## Folder Structure

```
Assets/
├── LICENSE              # CC BY-NC-ND 4.0
├── README.md            # This file
│
├── Sounds/              # Audio files
│   ├── generator_loop.wav
│   ├── power_on.wav
│   ├── power_off.wav
│   └── siren_ua.wav     # Ukrainian air raid siren
│
├── Icons/               # UI icons
│   ├── blackout.png
│   ├── generator.png
│   ├── battery.png
│   └── shelter.png      # Пункт Незламності icon
│
├── Textures/            # Building/UI textures
│   └── ...
│
└── Models/              # 3D models (if any)
    └── ...
```

## Adding Assets

When adding new assets:
1. Ensure you have rights to use them
2. They automatically fall under CC BY-NC-ND 4.0
3. Add attribution in this README if required

## Attribution

| Asset | Source | Author | License |
|-------|--------|--------|---------|
| Gepard.cok (Flakpanzer Gepard, 3D model) | [Sketchfab](https://sketchfab.com/3d-models/low-poly-flakpanzer-gepard-43d276c929184ad6822d9b868e1dbd26) | SIpriv ([profile](https://sketchfab.com/S1Priv)) | CC-BY-4.0 |
| DroneLauncher.cok (Shahed truck launcher, 3D model) | [Sketchfab](https://sketchfab.com/3d-models/shahed-truck-launcher-4391540756ef4349b8653828d09dff53) | 42manako ([profile](https://sketchfab.com/42manako)) | CC-BY-4.0 |
| Rocket.cok (Fatah-III/HD-1A missile, 3D model) | [Sketchfab](https://sketchfab.com/3d-models/fatah-iiihd-1a-supersonic-cruise-missile-62cfab4ce7cc4048a5465fbeddbe2630) | Chenzoss ([profile](https://sketchfab.com/Chenzoss)) | CC-BY-4.0 |
| Telecenter.cok — building body (Brutalist Building, 3D model) | [Sketchfab](https://sketchfab.com/3d-models/brutalist-building-113a8596c74e4ea98955887a6f2d8c1f) | matusgls ([profile](https://sketchfab.com/matusgls8)) | CC-BY-4.0 |
| Telecenter.cok — satellite dish (Satellite tower, 3D model) | [Sketchfab](https://sketchfab.com/3d-models/satellite-tower-509a2cc74d0742f7a6ba5d3036deab32) | Keeya ([profile](https://sketchfab.com/keeya)) | Sketchfab Free Standard |
| HIMARS.cok (M142 HIMARS, 3D model) | [Sketchfab](https://sketchfab.com/3d-models/m142-himars-free-model-9335f45a54e74ffea9301acf17615630) | Denys.Cherkasov ([profile](https://sketchfab.com/Denys.Cherkasov)) | CC-BY-4.0 |

> This work is based on "Low poly Flakpanzer gepard"
> (https://sketchfab.com/3d-models/low-poly-flakpanzer-gepard-43d276c929184ad6822d9b868e1dbd26)
> by SIpriv (https://sketchfab.com/S1Priv) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/).

> This work is based on "Shahed truck launcher"
> (https://sketchfab.com/3d-models/shahed-truck-launcher-4391540756ef4349b8653828d09dff53)
> by 42manako (https://sketchfab.com/42manako) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/).

> This work is based on "Fatah-III/HD-1A Supersonic Cruise Missile"
> (https://sketchfab.com/3d-models/fatah-iiihd-1a-supersonic-cruise-missile-62cfab4ce7cc4048a5465fbeddbe2630)
> by Chenzoss (https://sketchfab.com/Chenzoss) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/).

> This work is based on "Brutalist Building"
> (https://sketchfab.com/3d-models/brutalist-building-113a8596c74e4ea98955887a6f2d8c1f)
> by matusgls (https://sketchfab.com/matusgls8) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/).

> This work is based on "M142 HIMARS | FREE MODEL"
> (https://sketchfab.com/3d-models/m142-himars-free-model-9335f45a54e74ffea9301acf17615630)
> by Denys.Cherkasov (https://sketchfab.com/Denys.Cherkasov) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/).

## Sourcing models

Every shipped model so far came from Sketchfab under CC-BY (attribution rows above are the
obligation that comes with it). A licence that forbids redistribution inside a distributed
product does not work for us — the model ships inside the `.cok` in the mod.

**Vetted authors** — their pipeline matches our import without rework:

| Author | Why it fits | Taken from there |
|---|---|---|
| [Chenzoss](https://sketchfab.com/Chenzoss) | Every model: 1 material + 4 PBR textures, CC-BY, downloadable | `Rocket.cok` (Fatah-III/HD-1A) |
| [42manako](https://sketchfab.com/42manako) | 1 material, 28k faces | `DroneLauncher.cok` (Shahed truck launcher) |

**Checked and passing our filters, not used yet** (both Chenzoss, ~120k faces, need decimation):

- *S-400 Triumf missile launcher truck* — uid `0f630ad76a32435f928a0512c2b4f71a`; candidate for
  the counterattack missile launcher.
- *Barak 8 air defence system* — uid `2a027ce7ca664a50b8161b5240620f6d`; reserve for the
  air-defence line-up.

**Filters to apply before downloading anything:**

1. `isDownloadable` via the Sketchfab API — preview-only models waste the whole evaluation.
2. Material count, judged by the model's role — see `Model/CS2_EXPORT_GUIDE.md` §9. Mass-spawned
   threats stay single-material; building-sized static objects may carry several, but our
   Blender → Asset Editor path has never carried a multi-material asset through, so the first
   one pays for solving that.
3. Triangle budget for the role (`Model/CS2_EXPORT_GUIDE.md` §10). Decimating from 100k+ throws
   away the detail that made the model attractive, so prefer low-poly sources.
4. Licence permitting redistribution as part of the mod.
