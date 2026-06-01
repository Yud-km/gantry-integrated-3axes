/*
  sensor.c - NPN photoelectric sensor 3FE-DS304C
  Assumption: active LOW when object is detected.
*/

#include <avr/io.h>
#include <util/delay.h>
#include <stdint.h>

#include "cpu_map.h"
#include "sensor.h"

void sensor_init(void)
{
	SENSOR_DDR &= ~(1 << PHOTO_SENSOR_BIT);    // input
	SENSOR_PORT |= (1 << PHOTO_SENSOR_BIT);    // pull-up
}

uint8_t sensor_raw_active_low(void)
{
	return !(SENSOR_PIN & (1 << PHOTO_SENSOR_BIT));
}

uint8_t sensor_object_detected(void)
{
	uint8_t count = 0;

	// Debounce/filter noise: 5 samples, active if at least 3 LOW samples.
	for (uint8_t i = 0; i < 5; i++) {
		if (sensor_raw_active_low()) {
			count++;
		}
		_delay_us(200);
	}

	return (count >= 3) ? 1 : 0;
}