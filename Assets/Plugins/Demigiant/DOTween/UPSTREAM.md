# DOTween core

Original, unmodified core binary from Demigiant's official Unity 6000.3 test distribution.

- Upstream: https://github.com/Demigiant/dotween
- Commit: `bfb02b05551a45204352dc43bdc15b882147891a`
- Source directory: `UnityTests.Unity6000.3/Assets/Plugins/Demigiant/DOTween`
- SHA-256 of DOTween.dll: `5CEE7F2B16FD3BA509ECAC8BFC95EFC9347682956FC4C1EFDE14618176E34491`
- Copyright and setup notes: `readme.txt`
- License: https://dotween.demigiant.com/license.php
- Documentation: https://dotween.demigiant.com/documentation.php

The imported core reports version `1.3.030`. The game uses core Transform shortcuts and generic `DOTween.To`. Original `DOTweenModuleUtils`, `DOTweenModulePhysics` and `DOTweenModulePhysics2D` sources from the same commit supply DOTween's runtime module initializer and its referenced extensions. Optional UI/audio integrations are not installed. Tween owners explicitly stop their animations on destruction.
