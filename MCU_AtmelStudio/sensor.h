/* sensor.h - NPN photoelectric sensor E3F/3FE-DS30C4
   Active LOW: object detected => signal about 0V.
*/

#ifndef SENSOR_H
#define SENSOR_H

#include <stdint.h>

void sensor_init(void);
uint8_t sensor_raw_active_low(void);
uint8_t sensor_object_detected(void);

#endif
