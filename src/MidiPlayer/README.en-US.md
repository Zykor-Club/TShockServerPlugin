# MidiPlayer

- **Author**: 星梦 (primary), Mountain (assist), Ruyou (inspiration and guidance)
- **Version**: v1.4.5.5

> [!IMPORTANT]
> This plugin depends on the external `DryWetMidi` library.
> Make sure the dependency is installed before adding this plugin to the `ServerPlugins` folder.

## Features
- Parse MIDI files with DryWetMidi and play them through Terraria instruments
- Support Harp, Bell, Guitar Axe, Guitar Chords, and Drum mapping
- Support play, pause, resume, stop, queue playback, and paginated file list
- Support full-server shared playback, shared request queue, requester display, and mute
- Support music box binding: admins can bind a music box whose on/off state drives full-server playback; bound boxes cannot be destroyed
- New players join at the current playback position and share the same global queue
- Full-server playback removes the current song when no players are online; queue resumes 5 seconds after a player joins
- Use same-tick merging, same-style retrigger gating, and per-tick packet budget to reduce truncation and clipping
- Help list dynamically shows admin commands by permission; commands requiring player interaction are unavailable in the console
- Use DryWetMidi `Chord` objects for guitar chord detection
- Read `InstrumentNameEvent` / `ProgramNameEvent` as melody detection hints
- Use `GetDuration<TTimeSpan>()` for total song duration
- Keep original `NoteName` / `Octave` metadata

## Commands
| Syntax | Permission | Description |
|--------|:----------:|-------------|
| /midi | midiplayer.play | Show help |
| /midi play \<file or index\> [volume] | midiplayer.play | Play a MIDI file; enqueues while playing |
| /midi stop | midiplayer.play | Stop playback and clear the queue |
| /midi pause | midiplayer.play | Pause playback |
| /midi resume | midiplayer.play | Resume playback |
| /midi list [page] | midiplayer.play | List available MIDI files |
| /midi allon | midiplayer.admin | Enable full-server shared playback |
| /midi alloff | midiplayer.admin | Disable full-server shared playback |
| /midi bind | midiplayer.admin | Enter bind mode; right-click a music box to complete binding (30s timeout, players only) |
| /midi unbind | midiplayer.admin | Unbind the music box for the current world (console usable) |
| /midi plist [page] | midiplayer.play | Show current mode and playback queue |
| /midi dlist \<index\> | midiplayer.play | Remove a queued song |
| /midi slient | midiplayer.play | Toggle full-server playback mute |
| /midi merge [on/off] | midiplayer.admin | Toggle duplicate note merge; view status without argument |
| /midi limit [on/off] | midiplayer.admin | Toggle note rate limiting; view status without argument |
| /midi add \<index\> | midiplayer.admin | Add a song from /midi list to the adjust list; no argument shows the current list |
| /reload | tshock.cfg.reload | Reload plugin config via TShock |

> [!NOTE]
> The following commands require player interaction and cannot be run from the console (`TSPlayer.Server`): `play`/`stop`/`pause`/`resume`/`silent` and `bind`. All other commands (`list`/`allon`/`alloff`/`unbind`/`plist`/`dlist`/`merge`/`limit`) remain usable from the console.

## File Structure
The following directory is auto-created under `tshock/` on server startup:
```
tshock/
└── MidiPlayer/
    ├── MidiPlayerConfig.json    # Config file
    ├── InstrumentMap.json       # Instrument mapping config
    └── MidiSongs/               # MIDI files directory
```

## Configuration
Config path: `tshock/MidiPlayer/MidiPlayerConfig.json`

```json
{
  "音量校准": {
    "Harp": 1.33,
    "Bell": 1.20,
    "GuitarAxe": 1.13,
    "Drum": 0.50,
    "Guitar": 0.45
  },
  "预发送提前量(毫秒)": 5,
  "Bell同奏开关": true,
  "竖琴残响开关": true,
  "配置版本": 3,
  "每tick最大音符数": 10,
  "同Style重触发间隔(毫秒)": 35,
  "启用重复音符合并": true,
  "启用音符限流": true,
  "启用自动微调": false,
  "移调微调(半音)": 0,
  "需微调歌曲": [],
  "屏蔽手弹乐器音(防MIDI音高污染)": true
}
```

The legacy `MidiPlayerConfig.json` or `MidiPlayerSurroundConfig.json` (in the `tshock/` root) is migrated to `MidiPlayer/` on first load and the original files are deleted.

## Instrument Mapping Config
Instrument mapping path: `tshock/MidiPlayer/InstrumentMap.json`, auto-generated on first startup. Server owners can edit it freely and run `/reload` to apply changes.

```json
{
  "说明": "GM乐器编号 0-127 到泰拉瑞亚乐器的映射表。修改后执行 /reload 生效。可选值: Harp(竖琴), Bell(铃铛), GuitarAxe(吉他斧), Guitar(吉他和弦), Drum(鼓组)",
  "乐器映射表": [
    { "编号": 0,   "GM乐器": "原声大钢琴", "映射": "Harp" },
    { "编号": 8,   "GM乐器": "钢片琴",     "映射": "Bell" },
    { "编号": 47,  "GM乐器": "定音鼓",     "映射": "Drum" }
  ],
  "鼓组映射表": {
    "说明": "GM鼓音符编号到泰拉瑞亚鼓Style的映射。Style范围: 139-148。修改后执行 /reload 生效。",
    "映射": [
      { "音符": 35, "GM名称": "原声底鼓", "Style": 146 },
      { "音符": 38, "GM名称": "原声军鼓", "Style": 147 }
    ]
  },
  "吉他和弦定义": {
    "说明": "和弦识别用。根音: 0=C,2=D,4=E,5=F,7=G,9=A,11=B。音程: [4,7]=大三和弦, [3,7]=小三和弦。Style: 133-138。",
    "和弦": [
      { "根音": 0,  "和弦名": "C大调", "音程": [4, 7], "Style": 133 },
      { "根音": 2,  "和弦名": "D大调", "音程": [4, 7], "Style": 134 }
    ]
  }
}
```

### Mapping Fields
| Table | Description |
|-------|-------------|
| 乐器映射表 | 128 GM instruments (0-127) mapped to Terraria instruments; owners can remap any entry |
| 鼓组映射表 | GM drum notes mapped to Terraria drum styles |
| 吉他和弦定义 | Chord root/intervals mapped to pre-recorded chord styles |

- Available instruments: `Harp`, `Bell`, `GuitarAxe`, `Guitar`, `Drum`
- Deleting `InstrumentMap.json` and restarting regenerates the default config
- Run `/reload` after editing to apply changes without restarting the server

## Configuration Fields
| Field | Type | Default | Description |
|-------|------|:-------:|-------------|
| 音量校准 | object | - | Per-instrument volume calibration |
| 预发送提前量(毫秒) | int | 5 | Early send window in milliseconds |
| Bell同奏开关 | bool | true | Add bell harmony for high harp notes |
| 竖琴残响开关 | bool | true | Add harp reverb after harp notes |
| 配置版本 | int | 3 | Config migration version |
| 每tick最大音符数 | int | 10 | Max notes sent per tick per player |
| 同Style重触发间隔(毫秒) | int | 35 | Min retrigger interval for guitar/drum styles |
| 启用重复音符合并 | bool | true | Merge duplicate pitch notes in the same time window |
| 启用音符限流 | bool | true | Limit same-style notes to reduce truncation and clipping |
| 启用自动微调 | bool | false | Master switch: when on, only songs in "需微调歌曲" get whole-song transposition |
| 移调微调(半音) | int | 0 | Manual key offset applied on top of auto-transpose for adjusted songs |
| 需微调歌曲 | array | [] | File names of songs that need auto-adjustment (added via `/midi add <index>`) |
| 屏蔽手弹乐器音(防MIDI音高污染) | bool | true | Drop client hand-played instrument packets (InstrumentSound#58) server-side to prevent global-pitch pollution that caps/detunes MIDI high notes |

## Playback Notes
- MIDI folder: `tshock/MidiPlayer/MidiSongs`
- Supports `.mid` files and caches parsed metadata
- Running `/midi play` again while playing enqueues the next song
- Full-server mode shares one queue, prevents duplicate songs, and shows the requester
- In full-server mode, normal players can only remove their own requests; admins can remove any
- When no players are online, the current song is removed and queue advancement stops; queue resumes 5 seconds after a player joins
- Shows floating text on play start/finish: "Now playing xxx" / "xxx finished". In single-player mode it appears 2 tiles above the player's head; in full-server mode it appears 3 tiles above the bound music box (falls back to each player's head if unbound). The text color is random each time.

## Music Box Binding
- Run `/midi bind` to enter bind mode, then right-click the target music box to complete binding (auto-exits after 30 seconds; run `/midi bind` again to cancel)
- Binding is saved per-world in the `tshock.sqlite` database and auto-restored when switching worlds or restarting the server
- In full-server mode, the bound music box's on/off state drives full-server playback: on → resume, off → pause
- Bound music boxes cannot be mined, replaced, or destroyed by explosions
- Run `/midi unbind` to unbind the current world; any residual forced-pause state is cleared automatically

## MIDI Information Used
- `Velocity`: mapped to playback volume
- CC7 channel volume and CC11 expression: combined with velocity for volume
- `GetChords()` / `Chord`: used for guitar chord detection, falls back to 50ms grouping
- `InstrumentNameEvent` / `ProgramNameEvent`: used as melody detection hints
- `GetDuration<TTimeSpan>()`: calculates total duration
- `NoteName` / `Octave`: kept as metadata for debugging and future range policies
- Terraria playable pitch is roughly C4-C6 (MIDI 60-84); notes outside this range are folded by octave

## Changelog
### v1.4.5.5
- Fix high-note pollution: playing Harp/Bell/GuitarAxe before or during playback rewrites the client's global pitch `Main.musicPitch`, capping/detuning that instrument's high notes; the issue persisted until a client restart
- Add config `屏蔽手弹乐器音(防MIDI音高污染)` (default true): the server drops hand-played instrument packets (InstrumentSound#58) so they are not forwarded to other clients, fully blocking "hearing others play" pollution
- Note: notes you play locally are produced client-side and never pass through the server, so the plugin cannot block them; avoid playing instruments yourself during MIDI playback

### v1.4.5.4
- Add whole-song auto-transpose: new config fields `启用自动微调` (bool master switch) and `需微调歌曲` (file-name list); only listed songs are adjusted
- Add `/midi add <index>` command (admin) to add a song from `/midi list` to the adjust list; `/midi add` lists the current adjust list
- Remove the old numeric `音域超窗触发阈值` threshold; now gated by the adjust list, normal songs are completely unaffected
- Add `移调微调(半音)` config to manually fine-tune overall pitch on top of auto-transpose
- Deleted `.mid` files are automatically removed from the adjust list

### v1.4.5.2
- Add playback status floating text: shows "Now playing xxx" on play start and "xxx finished" on play end
- In single-player mode the text appears 2 tiles above the player's head; in full-server mode it appears 3 tiles above the bound music box (falls back to each player's head if unbound)
- Text color is random each time
- In full-server mode the music box on/off state is reflected in floating text: while the box is on, adding a song shows "Now playing xxx"; while off, it shows "xxx queued, music box is off, plays when opened"; when reopened it shows "Resuming xxx"

### v1.4.5.1
- Fix: a MIDI that already finished playing in full-server mode could not be re-added; the "already playing/queued" record is now cleared after playback ends, allowing the same MIDI to be requested again
- Fix: when all players leave during full-server playback, the current song's path was not cleared from the duplicate-detection record

### v1.4.5
- Add music box binding: `/midi bind` enters bind mode to right-click bind; `/midi unbind` unbinds the current world
- Binding persists in `tshock.sqlite` and auto-restores the per-world mapping on world switch/restart
- In full-server mode, the bound music box's on/off state drives full-server playback (on → resume, off → pause); bound boxes cannot be mined/replaced/destroyed
- Bind mode auto-exits after 30 seconds; running `/midi bind` again cancels it
- Help list now dynamically shows admin commands by permission; players without `midiplayer.admin` no longer see `bind`/`unbind` entries
- Restrict player-interaction commands from the console: `play`/`stop`/`pause`/`resume`/`silent`/`bind`
- Unify message styling via `UiHelper` (gradient colors + [i:4080] icon)

### v1.4.4
- Instrument mapping is now configurable: new `InstrumentMap.json`, auto-generates 128 GM instrument mappings on first startup
- Owners can freely remap GM instruments to Terraria instruments, drum styles, and guitar chord definitions
- Run `/reload` to hot-reload the mapping without recompiling the plugin

### v1.4.3
- Restructure: config and MIDI files moved to `tshock/MidiPlayer/` directory
- Full-server playback: remove current song on all players leave; queue resumes 5 seconds after a player joins
- Add `/midi merge [on/off]` to toggle duplicate note merging
- Add `/midi limit [on/off]` to toggle note rate limiting
- Config: add `启用重复音符合并` and `启用音符限流` entries; config version upgraded to v3

### v1.4.2
- Sync full-server playback state so new players join at the current progress
- Pause full-server playback and queue advancement when all players are offline
- Resume automatically when a player joins

### v1.4.1
- Add `/midi allon` / `/midi alloff` full-server shared playback
- Add `/midi plist` / `/midi pl` queue viewer with requester names
- Add `/midi dlist` queue removal with requester/admin permissions
- Add `/midi slient` / `/midi sl` mute for full-server playback
- Remove `/midi test`

### v1.4.0
- Use DryWetMidi `Chord` / `GetChords()` for guitar chord detection
- Use `InstrumentNameEvent` / `ProgramNameEvent` for melody detection
- Use `GetDuration<TTimeSpan>()` for total duration
- Add `NoteName`, `Octave`, and `ChordNoteNumbers` to the note model

### v1.3.0
- Remove surround mode and low-pitch filtering
- Bind plugin reload to TShock `/reload`
- Add gradient help messages
- Migrate config to `MidiPlayerConfig.json`

## Feedback
- Issues: https://github.com/Zykor-Club/TShockServerPlugin
