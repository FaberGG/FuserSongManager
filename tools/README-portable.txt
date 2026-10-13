Fuser Song Manager 2
====================

Manages your library of Fuser custom songs: see every song with its metadata and album art,
listen to it (with each part - beat, bass, loop, lead - switchable on and off), search and filter,
rate and annotate songs, enable or disable them, import new ones from .zip/.rar/.7z archives,
and start the game.

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

Good to know
------------
- Songs cannot be enabled, disabled or imported while Fuser is running.
- A song needs both its .pak and its .sig file to be enabled.
- The preview plays the song's parts as recorded, mixed together; the game adds its own effects on top.
  Audio is only played inside the app, never written to files.
- If something goes wrong, the log file says what happened and which file was involved.
