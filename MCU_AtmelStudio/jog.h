/*
  jog.h - Manual jog mode for WinForms
*/

#ifndef JOG_H
#define JOG_H

#include <stdint.h>

#define JOG_MODE_INCREMENTAL 0		//chay tuong doi(xet tu toa do hien tai)
#define JOG_MODE_ABSOLUTE    1		//chay tuyet doi(xet tu goc toa do)

#define JOG_STATUS_OK              0	//hop le
#define JOG_STATUS_BUSY            1	//dang ban
#define JOG_STATUS_TRAVEL_EXCEEDED 2	//vuot gioi han
#define JOG_STATUS_ALARM           3	//loi
#define JOG_STATUS_BAD_VALUE       4	//lenh sai hoac toa do k hop le

typedef struct {
  float x;
  float y;
  float z;
  float feed_rate;
  uint8_t mode;
} jog_command_t;

void jog_init(void);

uint8_t jog_execute(jog_command_t *cmd);
uint8_t jog_increment(float dx, float dy, float dz, float feed_mm_min);
uint8_t jog_absolute(float x, float y, float z, float feed_mm_min);

uint8_t jog_execute_line(char *line);

void jog_cancel(void);
uint8_t jog_is_running(void);

#endif
/*
  jog.h - Manual jog mode for WinForms
*/

#ifndef JOG_H
#define JOG_H

#include <stdint.h>

#define JOG_MODE_INCREMENTAL 0
#define JOG_MODE_ABSOLUTE    1

#define JOG_STATUS_OK              0
#define JOG_STATUS_BUSY            1
#define JOG_STATUS_TRAVEL_EXCEEDED 2
#define JOG_STATUS_ALARM           3
#define JOG_STATUS_BAD_VALUE       4

typedef struct {
  float x;
  float y;
  float z;
  float feed_rate;
  uint8_t mode;
} jog_command_t;

void jog_init(void);

uint8_t jog_execute(jog_command_t *cmd);
uint8_t jog_increment(float dx, float dy, float dz, float feed_mm_min);
uint8_t jog_absolute(float x, float y, float z, float feed_mm_min);

uint8_t jog_execute_line(char *line);

void jog_cancel(void);
uint8_t jog_is_running(void);

#endif
