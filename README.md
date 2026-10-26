<div align="center">

<img src="docs/icon.png" width="96" alt="">

# Spectre Control

**Take control of your HP laptop's fans, without myHP.**

Max fan speed with full CPU performance, a separate speed for each fan, and your setting survives restarts.

### [⬇️ Download Spectre Control](https://github.com/NeelTTV/spectre-fan-control/releases/latest/download/SpectreControl.exe)

*Windows 10 / 11 · 50 KB · nothing else to install · 🇬🇧 English / 🇫🇷 Français*

<img src="docs/screenshot.png" width="420" alt="Spectre Control window">

</div>

---

## The problem

On recent HP Spectre laptops, the **myHP** app only offers three modes, and none of them works well:

| myHP mode | Performance | Fans |
|---|---|---|
| Smart Sense | 🐌 throttled | medium |
| Cool | 🐌 throttled | ✅ strong |
| Performance | ✅ good… | ❌ weak, so the laptop overheats and slows down anyway |

**Spectre Control** gives you both at once: the CPU at full power **and** the fans at full speed.

## Install (2 minutes)

1. **Download** [SpectreControl.exe](https://github.com/NeelTTV/spectre-fan-control/releases/latest/download/SpectreControl.exe).
2. **Double-click** the file.
   > Windows may show **"Windows protected your PC"**. That's normal for a small free app without a paid code-signing certificate.
   > Click **More info**, then **Run anyway**.
3. Click **Yes** in the Windows prompt (administrator rights), then **Yes** to install.

Done 🎉 The app is on your **Desktop** and in the **Start menu**. You can **pin it to the taskbar** (right-click the icon → *Pin to taskbar*).

## How to use it

1. In myHP, pick the **Performance** mode (just once).
2. Open **Spectre Control** and choose a mode:
   - **Automatic**: HP's default fan control.
   - **Fixed speed**: both fans at the same speed. The *Quiet*, *Balanced* and *Maximum* buttons set the speed in one click.
   - **Separate speed for each fan**: a different speed for each fan.
3. Keep **Maximum CPU performance** checked to get all the power.
4. Click **Apply and keep after restart**.

Your setting is re-applied automatically at every startup and every wake from sleep. The app doesn't need to stay open.

**Language**: use the **EN / FR** buttons at the top right. The app is in English by default.

## FAQ

<details>
<summary><b>Is my laptop compatible?</b></summary>

Tested on the **HP Spectre x360 16 (16-aa0xxx, Intel Core Ultra 7 155H, RTX 4050)**.

Other recent HP laptops (Spectre, Envy, Omen, Victus…) use the same BIOS interface and should work. The app checks on its own at launch: if your laptop isn't compatible, it tells you and changes nothing.

Does it work on another model? [Let us know in an issue](https://github.com/NeelTTV/spectre-fan-control/issues) so we can add it to the list!
</details>

<details>
<summary><b>How do I go back to HP's default fan control?</b></summary>

Choose **Automatic**, click **Apply**, then **restart** the laptop. The BIOS keeps the fixed speed until the next restart: that's how the BIOS works, not the app.
</details>

<details>
<summary><b>Is it dangerous for my laptop?</b></summary>

No:
- the app uses HP's official BIOS commands, the same ones used by HP Omen gaming laptop tools;
- the speed can't go below **3,000 RPM**, so a fan can never stop;
- the CPU keeps its hardware overheating protection no matter what.

Max fans = more noise and a bit more wear, just like when myHP spins them up.
</details>

<details>
<summary><b>Why does the app need administrator rights?</b></summary>

Windows only lets administrators talk to the BIOS. The app asks **once**, during installation. After that it opens directly, thanks to a Windows scheduled task.
</details>

<details>
<summary><b>How do I uninstall it?</b></summary>

Open Spectre Control and click **Uninstall** at the bottom right. Everything is removed: the app, the shortcuts, the scheduled tasks and the settings.
</details>

<details>
<summary><b>My antivirus blocks the file</b></summary>

That's a common false positive with small unsigned apps that touch system settings. The full source code is in this repository, and you can build the app yourself (see below).
</details>

## Under the hood

HP laptops expose a BIOS interface through WMI (`root\wmi`, class `hpqBIntM`). Spectre Control sends command `0x20008` with:

| Type | Purpose |
|---|---|
| `0x10` | number of fans |
| `0x2D` | current speed of each fan (in hundreds of RPM) |
| `0x2E` | set a fixed speed for each fan (one byte per fan) |

On install, the app:
- copies itself to `C:\Program Files\SpectreControl`;
- creates two scheduled tasks:
  - **SpectreControl Apply** (SYSTEM account) re-applies the setting at startup, sign-in and wake from sleep;
  - **SpectreControl GUI** opens the app as administrator without a UAC prompt;
- stores its settings in `C:\ProgramData\SpectreControl\settings.ini`.

The *Maximum CPU performance* option tunes Windows power settings: aggressive processor boost, energy/performance preference at 0, no core parking.

### Build it yourself

No tools to install: the C# compiler ships with Windows.

```powershell
git clone https://github.com/NeelTTV/spectre-fan-control
cd spectre-fan-control
powershell -ExecutionPolicy Bypass -File build.ps1
```

The executable is created in `dist\SpectreControl.exe`.

---

<sub>Independent project, not affiliated with HP Inc. "HP", "Spectre" and "myHP" are trademarks of HP Inc. Use at your own risk, without warranty (MIT license).</sub>

<details>
<summary>🇫🇷 Français</summary>

**Spectre Control** permet de contrôler les ventilateurs des PC portables HP (testé sur le HP Spectre x360 16-aa0xxx) sans myHP : ventilateurs au maximum avec le processeur à fond, vitesse séparée pour chaque ventilateur, et le réglage est réappliqué à chaque démarrage et sortie de veille.

1. [Télécharge SpectreControl.exe](https://github.com/NeelTTV/spectre-fan-control/releases/latest/download/SpectreControl.exe) et double-clique dessus.
2. Si Windows affiche un avertissement : *Informations complémentaires → Exécuter quand même* (l'appli est gratuite et non signée).
3. Accepte la demande administrateur et choisis *Oui* pour installer. Un raccourci apparaît sur le Bureau et dans le menu Démarrer, épinglable à la barre des tâches.

Passe l'interface en français avec le bouton **FR** (en haut à droite). Dans myHP, choisis *Performance* une fois. Ensuite, dans Spectre Control, choisis *Automatique*, *Vitesse fixe* ou *Réglage séparé*, puis clique sur *Appliquer et garder au redémarrage*. Pour revenir à la gestion HP : *Automatique*, *Appliquer*, puis redémarre. Pour tout supprimer : *Désinstaller*.
</details>
