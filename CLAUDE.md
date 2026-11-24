# Wren: working rules

Wren is a public (MIT) C# / .NET 8 development environment for STM8S003F3-class
microcontrollers, driven through an ST-LINK V2 over SWIM. Its first target is the
Digicool Things "8-bit OLED Thing". See `docs/ProjectPlan.md`.

## About Michael
- He has written C++ and C# for decades. Everything that lands on his machine is C#,
  plus STM8 assembly for firmware, and one Arduino sketch for the Pico programmer
  (`firmware/PicoLink`, using pico-swimlib's BSD SWIM code; also shipped as a .uf2).
  pico-swimlib's own firmware is `firmware/pico-swimlib`, a prebuilt .uf2.
  No Python, no JavaScript, no leading underscore in names.
- He works on Windows: Visual Studio, Test Explorer, GitHub Desktop. Steps written for
  him are GUI steps, never command lines.
- He reviews and merges everything into main. Never push to main.
- Decisions of substance are his: ask with AskUserQuestion, recommendation first.
- Documents state current truth and open issues, never a change log.
- He is in Nelson, New Zealand (NZ time). "rt" means "Roger that".
- Keep answers simple and direct.

## Public-repo rules
- Digicool's factory firmware is in `firmware/digicool` with Greg's (Digicool's owner)
  permission; it is not MIT. Never commit Digicool's artwork.
- Keep ST's names (STM8, ST-LINK, SWIM) out of the project's own names. Using them to
  describe compatibility is fine.

## Layout
- `src/Wren` (Avalonia app), `src/Wren.Asm`, `src/Wren.Emu`, `src/Wren.Script` (BASIC
  compiler, FAT, debugging session), `src/Wren.Link` (ST-LINK V2 over WinUSB or a Pico
  over serial, SWIM, kernel install and program upload); tests in `tests/`.
  `tests/Wren.Link.Tests` has a simulated ST-LINK (`FakeStLink`), esp-stlink programmer
  (`FakeEspStLink`, as pico-swimlib answers) and PicoLink sketch (`FakePico`) that answer
  from the emulator.
- Wren.Link's protocol comes from the open-source stm8flash and OpenOCD (both GPL): read
  them for the protocol, never copy their code into this MIT repository.
- `firmware/kernel/` (the kernel, STM8 assembly), `firmware/include/stm8s003.inc`,
  `firmware/demo/HexDemo.asm` (an early test program), `firmware/PicoLink` (Wren's Pico
  programmer sketch and its .uf2) and `firmware/pico-swimlib` (pico-swimlib built
  unchanged); both use the same wiring, `docs/PicoLink.md`.
- `programs/` holds Wren BASIC programs; `OledThing.bas` is the Thing's own behaviour.
- `art/` holds the SVG sources for the wren; `src/Wren/Assets` holds what the app uses
  (`wren.ico`, `wren.png`, `wren-oled.png`).
- `docs/ProjectPlan.md` (plan, state), `docs/Design.md` (firmware design),
  `docs/Bytecode.md` (VM and FAT formats), `docs/OledThing.md` (pins).
- Infographics live in `docs/Infographics`: each PNG sits beside the one HTML file it is
  rendered from, and the render steps are in that file's opening comment.
- Michael works in Wren BASIC; the app never shows assembly or CPU registers.

## Reference data in the tests
- `tests/Wren.Asm.Tests/Data/Encodings.txt`: bytes from sdasstm8 (SDCC 4.2.0) for every
  instruction form. sdasstm8 gets forward branches to plain numbers wrong, so branch
  offsets there are computed (target - next address).
- `tests/Wren.Emu.Tests/Data/Flags.txt` and `Programs/CpuMix.expected`: results from
  ucsim 0.6.4 (`sstm8 -t STM8S103`). ucsim's Z flag after DIV/DIVW is inverted against
  PM0044; the tests skip that bit and Wren follows PM0044.
- Regenerating needs `sdcc` and `sdcc-ucsim` in a Linux container; Michael never needs them.

## Code rules
- Avalonia UI for the app, in a Windows 7 look: the Simple theme with `src/Wren/Styles/Win7.axaml`
  (Segoe UI 9 pt, Aero light blues, glossy buttons, gradient menu bar, captioned group boxes). Commands
  live in a standard menu bar with the usual shortcuts, not in toolbars. Each library is
  UI-free and has its own test project, so it can be tested headless.
- Firmware is STM8 assembly. There is no C compiler.
- Firmware must fit the S003's limits: 8 KB flash, 1 KB RAM, 128 B EEPROM. The kernel
  must end below the program store at $9440; a test checks.
- Wren.Asm directives beyond ST's syntax: `RELAX` (short JRA/CALLR where in reach),
  and overlapping output is an error. Kernel opcodes $30 up are one contiguous table:
  add new opcodes at the end and keep `Op.cs`, `Vm.inc` and `docs/Bytecode.md` in step.
- A cloud session cannot reach USB. Michael tests Wren.Link on Windows.
- The Pico sketch is compile-checked in a session with the Pico SDK and
  `firmware/PicoLink/sdk` (which also builds its .uf2); the SDK and the ARM toolchain are
  installed outside the clone. SWIM itself can only be tested on Michael's hardware.
