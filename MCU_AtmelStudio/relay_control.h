/*
  relay_control.h - Relay control for fan and electromagnet
*/

#ifndef RELAY_CONTROL_H
#define RELAY_CONTROL_H

#include <stdint.h>

void relay_init(void);

void relay_fan_on(void);
void relay_fan_off(void);

void relay_magnet_on(void);
void relay_magnet_off(void);

void relay_all_off(void);

#endif
