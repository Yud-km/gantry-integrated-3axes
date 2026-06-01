/*
  planner.h - tinh quy dao chuyen dong
  k phat xung truc tiep nhu stepper, nhan toa do can den roi tinh:
  X c?n bao nhiêu step
  Y c?n bao nhiêu step
  Z c?n bao nhiêu step
  chi?u quay t?ng tr?c
  qu?ng ðý?ng th?c
  t?c ð?
  gia t?c
  ði?m tãng t?c / gi?m t?c
*/

#ifndef PLANNER_H
#define PLANNER_H

#include <stdint.h>
#include <stdbool.h>

#define N_AXIS 3
#define X_AXIS 0
#define Y_AXIS 1
#define Z_AXIS 2

#ifndef BLOCK_BUFFER_SIZE
#define BLOCK_BUFFER_SIZE 16
#endif

#ifndef bit
#define bit(n) (1 << (n))
#endif

#define PLAN_OK true
#define PLAN_EMPTY_BLOCK false

//co cau co khi va chuyen dong cua may
/*
steps_per_mm: s? step c?n ð? tr?c ði ðý?c 1 mm
X/Y dùng con lãn D24 ? 42.44 step/mm
Z dùng vít me 8mm/v?ng ? 400 step/mm

max_rate: t?c ð? t?i ða t?ng tr?c, ðõn v? mm/phút
vi du max_rate[X_AXIS] = 2500.0f; => toc do toi da cua truc x la 2500mm/min

acceleration: gia t?c t?ng tr?c, ðõn v? mm/s²
vi du: acceleration[X_AXIS] = 180.0f; => truc x tang toc voi gia toc 180 mm/s2

junction_deviation: Dùng ð? tính t?c ð? khi ði qua góc cua gi?a 2 ðo?n.
Giá tr? nh? th? máy gi?m t?c nhi?u hõn khi ð?i hý?ng, ch?y an toàn hõn nhýng ch?m hõn.
*/
typedef struct {
  float steps_per_mm[N_AXIS];    // X/Y roller, Z screw
  float max_rate[N_AXIS];        // mm/min
  float acceleration[N_AXIS];    // mm/sec^2
  float junction_deviation;      // mm
} planner_settings_t;


//thong tin di kem mot lenh di chuyen
typedef struct {
  float feed_rate;               // mm/min - toc do yeu cau
  uint8_t rapid_motion;          // 1 = use axis-limited max rate
} plan_line_data_t;


//thong tin cua 1 block chuyen dong vi du di tu (0, 0) den (50, 50) planner tao 1 block
/*
steps[N_axis]: so step tung truc can chay
vi du: X = 50 mm × 42.44 = 2122 step
		Y = 50 mm × 42.44 = 2122 step
		Z = 0 step
*/
typedef struct {
  uint32_t steps[N_AXIS];		
  uint32_t step_event_count;	//so step max dung cho noi suy Bresenham
  uint8_t direction_bits;		//luu chieu quay tung truc

  float millimeters;			//chieu dai duong di vi du 0,0 den 50, 50 =>s = sqrt(50² + 50²)= 70.71 mm
  float programmed_rate;         // mm/min - toc do yeu cau
  float rapid_rate;              // mm/min - toc do max
  float nominal_speed;           // mm/sec - toc do dinh danh
  float entry_speed;             // mm/sec - toc do dau vao block
  float exit_speed;              // mm/sec - toc do ra khoi cuoi block
  float max_entry_speed;         // mm/sec - toc do vao lon nhat cho phep
  float acceleration;            // mm/sec^2 - gia toc cua block

  float unit_vec[N_AXIS];		//huong chuyen dong cua block
} plan_block_t;

extern planner_settings_t planner_settings;

//khoi tao planner dung khi bat dau chuong trinh
void plan_init(void);

//dung khan cap
void plan_reset(void);

//xoa buffer chuyen dong
void plan_reset_buffer(void);

//dat vi tri hien tai (mm)
void plan_set_position_mm(float x, float y, float z);

//dat vi tri hien tai (step)
void plan_set_position_steps(int32_t x, int32_t y, int32_t z);

//lay vi tri hien tai cua planner(mm)
void plan_get_position_mm(float *pos);

//nhan toa do dich
uint8_t plan_buffer_line(float *target, plan_line_data_t *pl_data);

//ktra buffer day k
uint8_t plan_check_full_buffer(void);

//ktra rong
uint8_t plan_is_buffer_empty(void);

//lay block hien tai cho stepper
plan_block_t *plan_get_current_block(void);

//xoa block vua chay xong
void plan_discard_current_block(void);

//tinh vi tri ke tiep trong buffer
uint8_t plan_next_block_index(uint8_t block_index);

#endif
