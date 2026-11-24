#include "arduino_shim.h"
#include "../PicoLink.ino"
int main() { stdio_init_all(); setup(); for (;;) loop(); }
