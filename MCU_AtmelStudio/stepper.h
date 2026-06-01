/*
  stepper.h - STEP/DIR executor for gantry crane
*/

#ifndef STEPPER_H
#define STEPPER_H

#include <stdint.h>
#include "planner.h"

//khoi tao chan va timer
void stepper_init(void);

void st_reset(void);
void st_wake_up(void);
void st_go_idle(void);

//chuan bi block chay
void st_prep_buffer(void);
uint8_t st_is_running(void);

void st_set_position_zero(void);

//khai bao vi tri hien tai
void st_set_position_mm(float x, float y, float z);
void st_generate_step_dir_invert_masks(void);

extern volatile int32_t sys_position[N_AXIS];

#endif
