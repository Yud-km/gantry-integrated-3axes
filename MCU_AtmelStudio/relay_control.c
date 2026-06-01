/*
  relay_control.c - 2 channel relay control
*/

#include <avr/io.h>

#include "cpu_map.h"
#include "relay_control.h"

static void relay_set(uint8_t bit_id, uint8_t on)
{
#if RELAY_ACTIVE_LOW
  if (on) RELAY_PORT &= ~(1 << bit_id);
  else RELAY_PORT |= (1 << bit_id);
#else
  if (on) RELAY_PORT |= (1 << bit_id);
  else RELAY_PORT &= ~(1 << bit_id);
#endif
}

void relay_init(void)
{
  RELAY_DDR |= RELAY_MASK;
  relay_all_off();
}

void relay_fan_on(void)     { relay_set(FAN_RELAY_BIT, 1); }
void relay_fan_off(void)    { relay_set(FAN_RELAY_BIT, 0); }

void relay_magnet_on(void)  { relay_set(MAGNET_RELAY_BIT, 1); }
void relay_magnet_off(void) { relay_set(MAGNET_RELAY_BIT, 0); }

void relay_all_off(void)
{
  relay_fan_off();
  relay_magnet_off();
}
