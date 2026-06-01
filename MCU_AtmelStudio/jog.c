/*
  jog.c - GRBL-like jog module for manual WinForms control
  FIXED VERSION:
    - Removed lcd_print_state() calls.
    - Does not depend on LCD/TWI.
    - Safe to use with test_serial_jog.c minimal init.
*/

#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <math.h>

#include "jog.h"
#include "planner.h"
#include "stepper.h"
#include "motion_control.h"
#include "system.h"

void jog_init(void)
{
  // Reserved for future jog settings.
}


//lay gia tru tu lenh dieu khien 
//vi du JOGI X10 Y5 Z0 F1000
static uint8_t parse_word_float(char *line, char word, float *value)
{
  char *p = line;

  while (*p) {
    if (toupper((unsigned char)*p) == word) {
      *value = atof(p + 1);
      return 1;
    }
    p++;
  }

  return 0;
}


//xu ly lenh 
uint8_t jog_execute(jog_command_t *cmd)
{
	//ktra lenh
  if (!cmd) return JOG_STATUS_BAD_VALUE;

	//ktra trang thai cua he
  if (sys.state & STATE_ALARM) {
    return JOG_STATUS_ALARM;
  }

  if (st_is_running()) {
    return JOG_STATUS_BUSY;
  }

	//ktra toc do
  if (cmd->feed_rate <= 0.0f) {
    return JOG_STATUS_BAD_VALUE;
  }

	//lay vi tri hien tai - step
  float current[N_AXIS];
  float target[N_AXIS];
	//doi don vi
  system_convert_array_steps_to_mpos(current, sys_position);
	//tinh toa do dich
  if (cmd->mode == JOG_MODE_INCREMENTAL) {
    target[X_AXIS] = current[X_AXIS] + cmd->x;
    target[Y_AXIS] = current[Y_AXIS] + cmd->y;
    target[Z_AXIS] = current[Z_AXIS] + cmd->z;
  } else {
    target[X_AXIS] = cmd->x;
    target[Y_AXIS] = cmd->y;
    target[Z_AXIS] = cmd->z;
  }

	//ktra toa do co hop le k
  if (isnan(target[X_AXIS]) || isnan(target[Y_AXIS]) || isnan(target[Z_AXIS])) {
    return JOG_STATUS_BAD_VALUE;
  }
	//ktra gioi han
  if (system_check_travel_limits(target)) {
    return JOG_STATUS_TRAVEL_EXCEEDED;
  }
	
	
  sys.state = STATE_JOG;
	/*
  if (!mc_line(target[X_AXIS], target[Y_AXIS], target[Z_AXIS], cmd->feed_rate, 0)) {
    sys.state = STATE_IDLE;
    return JOG_STATUS_BAD_VALUE;
  }
  */

	//bat stepper
  st_prep_buffer();
  st_wake_up();

  return JOG_STATUS_OK;
}


//han chay o mode tuong doi
uint8_t jog_increment(float dx, float dy, float dz, float feed_mm_min)
{
  jog_command_t cmd;

  cmd.x = dx;
  cmd.y = dy;
  cmd.z = dz;
  cmd.feed_rate = feed_mm_min;
  cmd.mode = JOG_MODE_INCREMENTAL;

  return jog_execute(&cmd);
}


//chay mode tuyet doi
uint8_t jog_absolute(float x, float y, float z, float feed_mm_min)
{
  jog_command_t cmd;

  cmd.x = x;
  cmd.y = y;
  cmd.z = z;
  cmd.feed_rate = feed_mm_min;
  cmd.mode = JOG_MODE_ABSOLUTE;

  return jog_execute(&cmd);
}


//nhan lenh trucc tiep tu winform
uint8_t jog_execute_line(char *line)
{
  if (!line) return JOG_STATUS_BAD_VALUE;

  if (strncmp(line, "JOGC", 4) == 0) {
    jog_cancel();
    return JOG_STATUS_OK;
  }

  jog_command_t cmd;
  float current[N_AXIS];

  system_convert_array_steps_to_mpos(current, sys_position);

  cmd.x = 0.0f;
  cmd.y = 0.0f;
  cmd.z = 0.0f;
  cmd.feed_rate = 1000.0f;
  cmd.mode = JOG_MODE_INCREMENTAL;

  if (strncmp(line, "JOGA", 4) == 0) {
    cmd.mode = JOG_MODE_ABSOLUTE;
    cmd.x = current[X_AXIS];
    cmd.y = current[Y_AXIS];
    cmd.z = current[Z_AXIS];
  } else if (strncmp(line, "JOGI", 4) == 0) {
    cmd.mode = JOG_MODE_INCREMENTAL;
  } else {
    return JOG_STATUS_BAD_VALUE;
  }

  parse_word_float(line, 'X', &cmd.x);
  parse_word_float(line, 'Y', &cmd.y);
  parse_word_float(line, 'Z', &cmd.z);
  parse_word_float(line, 'F', &cmd.feed_rate);

  return jog_execute(&cmd);
}


// huy lenh jog
void jog_cancel(void)
{
  st_go_idle();
  plan_reset();
  sys.state = STATE_IDLE;
}


//ktra he dang chay k
uint8_t jog_is_running(void)
{
  if (st_is_running()) return 1;

  if (sys.state == STATE_JOG) {
    sys.state = STATE_IDLE;
  }

  return 0;
}
