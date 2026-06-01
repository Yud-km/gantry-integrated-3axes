#include "grbl.h"

int main(void)
{
	gantry_init();

	while (1) {
		gantry_loop_once();
	}

	return 0;
}