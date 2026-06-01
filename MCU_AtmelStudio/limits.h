/*
 * limits.h - CNC Shield V3 active LOW limit switches
 */

#ifndef LIMITS_H
#define LIMITS_H

#include <stdint.h>
#include "planner.h"

#ifndef bit
#define bit(n) (1 << (n))
#endif

#define LIMIT_X       bit(0)
#define LIMIT_Y       bit(1)
#define LIMIT_Z_PLUS  bit(2)
#define LIMIT_Z       LIMIT_Z_PLUS

#define LIMIT_X_MIN   bit(3)
#define LIMIT_X_MAX   bit(4)
#define LIMIT_Y_MIN   bit(5)
#define LIMIT_Y_MAX   bit(6)

#define HOMING_CYCLE_X    bit(X_AXIS)
#define HOMING_CYCLE_Y    bit(Y_AXIS)
#define HOMING_CYCLE_Z    bit(Z_AXIS)
#define HOMING_CYCLE_ALL  (HOMING_CYCLE_X | HOMING_CYCLE_Y)

#define HOMING_OK          0
#define HOMING_FAIL_X      1
#define HOMING_FAIL_Y      2

void limits_init(void);
void limits_enable(void);
void limits_disable(void);

uint8_t limits_get_state(void);

uint8_t limits_x(void);
uint8_t limits_y(void);
uint8_t limits_z_plus(void);
uint8_t limits_z(void);

/* Compatible old names. */
uint8_t limits_x_min(void);
uint8_t limits_x_max(void);
uint8_t limits_y_min(void);
uint8_t limits_y_max(void);

uint8_t limits_hard_alarm(void);
void limits_clear_hard_alarm(void);

uint8_t limits_check_home(void);
uint8_t limits_go_home(uint8_t cycle_mask);

#endif
