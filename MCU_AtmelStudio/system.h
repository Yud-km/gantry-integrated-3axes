/*
  system.h - GRBL-like system state and realtime flags
*/

#ifndef SYSTEM_H
#define SYSTEM_H

#include <stdint.h>
#include "planner.h"

#ifndef bit
#define bit(n) (1 << (n))
#endif

// Realtime flags.
#define EXEC_STATUS_REPORT  bit(0)
#define EXEC_CYCLE_START    bit(1)
#define EXEC_CYCLE_STOP     bit(2)
#define EXEC_RESET          bit(3)
#define EXEC_ALARM          bit(4)
#define EXEC_HOME           bit(5)

// Alarm codes.
#define EXEC_ALARM_NONE             0
#define EXEC_ALARM_HARD_LIMIT       1
#define EXEC_ALARM_SOFT_LIMIT       2
#define EXEC_ALARM_HOMING_FAIL      3
#define EXEC_ALARM_SENSOR_TIMEOUT   4
#define EXEC_ALARM_Z_CONTACT_FAIL   5
#define EXEC_ALARM_ESTOP            6

// System states.
#define STATE_IDLE          0
#define STATE_ALARM         bit(0)
#define STATE_HOMING        bit(1)
#define STATE_CYCLE         bit(2)
#define STATE_JOG           bit(3)
#define STATE_HOLD          bit(4)

typedef struct {
  uint8_t state;
  uint8_t abort;
  uint8_t homed;
  uint8_t soft_limit;

  uint8_t f_override;
  uint8_t r_override;

  float max_travel[N_AXIS];     // positive workspace limit in mm
} system_t;

extern system_t sys;
extern volatile uint8_t sys_rt_exec_state;
extern volatile uint8_t sys_rt_exec_alarm;

void system_init(void);
void system_reset(void);
void system_execute_realtime(void);

uint8_t system_check_travel_limits(float *target);
float system_convert_axis_steps_to_mpos(volatile int32_t *steps, uint8_t idx);
void system_convert_array_steps_to_mpos(float *position, volatile int32_t *steps);

void system_set_exec_state_flag(uint8_t mask);
void system_clear_exec_state_flag(uint8_t mask);
void system_set_exec_alarm(uint8_t code);
void system_clear_exec_alarm(void);

void system_report_status(void);
void system_report_alarm(uint8_t alarm_code);

#endif
