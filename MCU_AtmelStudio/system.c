/*
  system.c - GRBL-like realtime system manager
*/

#include <avr/io.h>
#include <avr/interrupt.h>
#include <stdlib.h>
#include <string.h>

#include "cpu_map.h"
#include "system.h"
#include "stepper.h"
#include "planner.h"
#include "serial.h"
#include "relay_control.h"
#include "lcd.h"

system_t sys;
volatile uint8_t sys_rt_exec_state = 0;
volatile uint8_t sys_rt_exec_alarm = 0;

static void write_float(float value)
{
  char buf[16];
  dtostrf(value, 0, 2, buf);
  serial_write_string(buf);
}

void system_init(void)
{
  memset(&sys, 0, sizeof(system_t));

  sys.state = STATE_IDLE;
  sys.f_override = 100;
  sys.r_override = 100;

  // Adjust to real crane travel.
  sys.max_travel[X_AXIS] = 220.0f;
  sys.max_travel[Y_AXIS] = 120.0f;
  sys.max_travel[Z_AXIS] = 60.0f;
}

void system_reset(void)
{
  uint8_t sreg = SREG;
  cli();

  sys_rt_exec_state = 0;
  sys_rt_exec_alarm = 0;
  sys.abort = 0;
  sys.state = STATE_IDLE;

  SREG = sreg;

  st_go_idle();
  plan_reset();
  relay_all_off();
}

uint8_t system_check_travel_limits(float *target)
{
  for (uint8_t i = 0; i < N_AXIS; i++) {
    if (target[i] < 0.0f) return 1;
    if (target[i] > sys.max_travel[i]) return 1;
  }
  return 0;
}

float system_convert_axis_steps_to_mpos(volatile int32_t *steps, uint8_t idx)
{
  return (float)steps[idx] / planner_settings.steps_per_mm[idx];
}

void system_convert_array_steps_to_mpos(float *position, volatile int32_t *steps)
{
  for (uint8_t i = 0; i < N_AXIS; i++) {
    position[i] = system_convert_axis_steps_to_mpos(steps, i);
  }
}

void system_report_status(void)
{
  float mpos[N_AXIS];
  system_convert_array_steps_to_mpos(mpos, sys_position);

  serial_write_string("<");

  if (sys.state == STATE_IDLE) serial_write_string("Idle");
  else if (sys.state & STATE_ALARM) serial_write_string("Alarm");
  else if (sys.state & STATE_HOMING) serial_write_string("Home");
  else if (sys.state & STATE_JOG) serial_write_string("Jog");
  else if (sys.state & STATE_CYCLE) serial_write_string("Run");
  else serial_write_string("Unknown");

  serial_write_string("|MPos:");
  write_float(mpos[X_AXIS]);
  serial_write(',');
  write_float(mpos[Y_AXIS]);
  serial_write(',');
  write_float(mpos[Z_AXIS]);
  serial_write_string("|Homed:");
  serial_write(sys.homed ? '1' : '0');
  serial_write_ln(">");
}

void system_report_alarm(uint8_t alarm_code)
{
  serial_write_string("ALARM:");
  serial_write('0' + alarm_code);
  serial_write_ln("");

  lcd_print_state("ALARM");
}

void system_execute_realtime(void)
{
  uint8_t rt_exec = sys_rt_exec_state;

  if (rt_exec & EXEC_RESET) {
    system_reset();
    serial_write_ln("reset:ok");
    return;
  }

  if (sys_rt_exec_alarm != EXEC_ALARM_NONE) {
    st_go_idle();
    plan_reset();
    relay_all_off();

    sys.state = STATE_ALARM;
    system_report_alarm(sys_rt_exec_alarm);

    system_clear_exec_state_flag(EXEC_ALARM);
  }

  if (rt_exec & EXEC_CYCLE_STOP) {
    st_go_idle();
    plan_reset();
    relay_all_off();

    sys.state = STATE_IDLE;
    system_clear_exec_state_flag(EXEC_CYCLE_STOP);

    serial_write_ln("cycle:stop");
    lcd_print_state("STOP");
  }

  if (rt_exec & EXEC_STATUS_REPORT) {
    system_report_status();
    system_clear_exec_state_flag(EXEC_STATUS_REPORT);
  }
}

void system_set_exec_state_flag(uint8_t mask)
{
  uint8_t sreg = SREG;
  cli();
  sys_rt_exec_state |= mask;
  SREG = sreg;
}

void system_clear_exec_state_flag(uint8_t mask)
{
  uint8_t sreg = SREG;
  cli();
  sys_rt_exec_state &= ~mask;
  SREG = sreg;
}

void system_set_exec_alarm(uint8_t code)
{
  uint8_t sreg = SREG;
  cli();
  sys_rt_exec_alarm = code;
  sys_rt_exec_state |= EXEC_ALARM;
  SREG = sreg;
}

void system_clear_exec_alarm(void)
{
  uint8_t sreg = SREG;
  cli();
  sys_rt_exec_alarm = EXEC_ALARM_NONE;
  SREG = sreg;
}
