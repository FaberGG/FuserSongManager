Fuser Song Manager 2
====================

Manages your library of Fuser custom songs, in a look inspired by the game's crate: every song as
its album art, the selected one in a side panel where you can listen to it in a loop (with each
part - beat, bass, loop, lead - on its own disc, switchable on and off), rate it and add notes.
Search and filter, enable or disable songs, import new ones from .zip/.rar/.7z archives (also a
"download all" archive with each song's own .zip or .rar inside), and start the game.

Requirements
------------
Windows 10 or 11, 64-bit. Nothing else to install.

Running it
----------
Unzip anywhere and run FuserSongManager.exe. It is a portable app: there is no installer.

Windows may show a SmartScreen warning the first time, because the program is not signed.
Choose "More info" > "Run anyway".

The first time, it asks for the Fuser install folder (the one that contains Fuser.exe).

Coming from Fuser Custom Song Manager v1
----------------------------------------
On first run the app finds the v1 settings and library and offers to bring your ratings, notes
and authors. The v1 files are only read, never changed, so v1 keeps working if you go back to it.

Where your data is
------------------
%LocalAppData%\FuserSongManager
    settings.json   your settings
    songs.sqlite    the library (ratings, notes, authors and the metadata read from the paks)
    logs\           one log file per day, kept for two weeks (Settings > Open log folder)
    art\            album art taken from the paks, so it shows instantly next time (safe to delete)

Your songs stay where they are: Fuser\Content\Paks\custom_songs (enabled) and
Fuser\Content\Paks\disabled_songs (disabled).

Keyboard
--------
Z            autoplay on/off: the song plays as soon as you select it
Space        play / pause
Enter        enable or disable the selected songs
Arrow keys   move between songs; Ctrl+click and Shift+click select several; Ctrl+A selects all
Ctrl+F       search          Ctrl+I   import          F5   rescan          Esc   clear the selection

Good to know
------------
- Songs cannot be enabled, disabled or imported while Fuser is running.
- A song needs both its .pak and its .sig file to be enabled.
- The preview loops like the game does (four phrases of eight bars) and plays the parts as recorded,
  mixed together; the game adds its own effects on top. Audio is only played inside the app, never
  written to files.
- The first start after updating reads every song once more, to learn the instrument of each part.
- If something goes wrong, the log file says what happened and which file was involved.

License
-------
Fuser Song Manager is free software under the MIT License (LICENSE.txt). The instrument icons come from
the game FUSER and belong to their owners; third-party components are listed in THIRD-PARTY-NOTICES.txt.
Not affiliated with Harmonix, NCSoft or Epic Games.
