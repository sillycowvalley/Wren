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
- He is on Sydney time. "rt" means "Roger that".
- Keep answers simple and direct.

## Public-repo rules
- Never commit Digicool's .s19 firmware or artwork.
- Keep ST's names (STM8, ST-LINK, SWIM) out of the project's own names. Using them to
  describe compatibility is fine.

## Code rules
- Avalonia UI for the app. Each library is UI-free and has its own test project, so it
  can be tested headless.
- Firmware is STM8 assembly. There is no C compiler.
- Firmware must fit the S003's limits: 8 KB flash, 1 KB RAM, 128 B EEPROM.
- A cloud session cannot reach USB. Michael tests Wren.Link on Windows.
