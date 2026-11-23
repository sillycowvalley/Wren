# Wren: working rules

Wren is a public (MIT) C# / .NET 8 development environment for STM8S003F3-class
microcontrollers, driven through an ST-LINK V2 over SWIM. Its first target is the
Digicool Things "8-bit OLED Thing". See `docs/ProjectPlan.md`.

## About Michael
- He has written C++ and C# for decades. Everything that lands on his machine is C#,
  plus STM8 assembly for firmware. No Python, no JavaScript, no leading underscore in names.
- He works on Windows: Visual Studio, Test Explorer, GitHub Desktop. Steps written for
  him are GUI steps, never command lines.
- He reviews and merges everything into main. Never push to main.
- Decisions of substance are his: ask with AskUserQuestion, recommendation first.
- Documents state current truth and open issues, never a change log.
- He is in Nelson, New Zealand (NZ time). "rt" means "Roger that".
- Keep answers simple and direct.

## Public-repo rules
- Never commit Digicool's .s19 firmware or artwork.
- Keep ST's names (STM8, ST-LINK, SWIM) out of the project's own names. Using them to
  describe compatibility is fine.

## Layout
- `src/Wren` (Avalonia app), `src/Wren.Asm`, `src/Wren.Emu`; tests in `tests/`.
- `firmware/include/stm8s003.inc` (register addresses), `firmware/demo/HexDemo.asm`.
- `docs/ProjectPlan.md` (plan, state, open questions), `docs/OledThing.md` (pins).

## Reference data in the tests
- `tests/Wren.Asm.Tests/Data/Encodings.txt`: bytes from sdasstm8 (SDCC 4.2.0) for every
  instruction form. sdasstm8 gets forward branches to plain numbers wrong, so branch
  offsets there are computed (target - next address).
- `tests/Wren.Emu.Tests/Data/Flags.txt` and `Programs/CpuMix.expected`: results from
  ucsim 0.6.4 (`sstm8 -t STM8S103`). ucsim's Z flag after DIV/DIVW is inverted against
  PM0044; the tests skip that bit and Wren follows PM0044.
- Regenerating needs `sdcc` and `sdcc-ucsim` in a Linux container; Michael never needs them.

## Code rules
- Avalonia UI for the app. Each library is UI-free and has its own test project, so it
  can be tested headless.
- Firmware is STM8 assembly. There is no C compiler.
- Firmware must fit the S003's limits: 8 KB flash, 1 KB RAM, 128 B EEPROM.
- A cloud session cannot reach USB. Michael tests Wren.Link on Windows.
