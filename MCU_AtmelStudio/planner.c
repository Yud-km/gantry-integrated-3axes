/*
  planner.c - GRBL-like look-ahead planner for gantry crane

  Mechanical settings:
    Motor: 200 full steps/rev
    Microstep: 1/16
    X/Y roller diameter: 24 mm
    Z lead screw: 8 mm/rev

  X/Y steps/mm = 200*16/(pi*24) = 42.44
  Z steps/mm   = 200*16/8       = 400
*/

#include <math.h>
#include <stdlib.h>
#include <string.h>

#include "cpu_map.h"
#include "planner.h"

planner_settings_t planner_settings = {
  .steps_per_mm = {
    42.44f,    // X roller D24
    42.44f,    // Y roller D24
    400.0f     // Z screw 8 mm/rev
  },
  .max_rate = {
    2500.0f,   // X mm/min
    2500.0f,   // Y mm/min
    600.0f     // Z mm/min
  },
  .acceleration = {
    180.0f,    // X mm/sec^2
    180.0f,    // Y mm/sec^2
    60.0f      // Z mm/sec^2
  },
  .junction_deviation = 0.02f
};


//planner them block vao head, stepper lay block tu tail
static plan_block_t block_buffer[BLOCK_BUFFER_SIZE];
static uint8_t block_buffer_tail;
static uint8_t block_buffer_head;
static uint8_t next_buffer_head;

static int32_t planner_position_steps[N_AXIS];
static float previous_unit_vec[N_AXIS];
static float previous_nominal_speed;


uint8_t plan_next_block_index(uint8_t block_index)
{
  block_index++;
  if (block_index == BLOCK_BUFFER_SIZE) block_index = 0;
  return block_index;
}


static uint8_t plan_prev_block_index(uint8_t block_index)
{
  if (block_index == 0) block_index = BLOCK_BUFFER_SIZE;
  return block_index - 1;
}


/*
v? trí hi?n t?i = 0 step
hý?ng block trý?c = 0
t?c ð? block trý?c = 0
xóa buffer
*/
void plan_init(void)
{
  memset(planner_position_steps, 0, sizeof(planner_position_steps));
  memset(previous_unit_vec, 0, sizeof(previous_unit_vec));
  previous_nominal_speed = 0.0f;
  plan_reset_buffer();
}


void plan_reset(void)
{
	//xoa thong tin block trc va buffer => xoa cac lenh dang cho vi tri thi giu nguyen
  memset(previous_unit_vec, 0, sizeof(previous_unit_vec));
  previous_nominal_speed = 0.0f;
  plan_reset_buffer();
}


void plan_reset_buffer(void)
{
  memset(block_buffer, 0, sizeof(block_buffer));
  block_buffer_tail = 0;
  block_buffer_head = 0;
  next_buffer_head = 1;
}


void plan_set_position_mm(float x, float y, float z)
{
  planner_position_steps[X_AXIS] = lroundf(x * planner_settings.steps_per_mm[X_AXIS]);
  planner_position_steps[Y_AXIS] = lroundf(y * planner_settings.steps_per_mm[Y_AXIS]);
  planner_position_steps[Z_AXIS] = lroundf(z * planner_settings.steps_per_mm[Z_AXIS]);
}


void plan_set_position_steps(int32_t x, int32_t y, int32_t z)
{
  planner_position_steps[X_AXIS] = x;
  planner_position_steps[Y_AXIS] = y;
  planner_position_steps[Z_AXIS] = z;
}


void plan_get_position_mm(float *pos)
{
  pos[X_AXIS] = (float)planner_position_steps[X_AXIS] / planner_settings.steps_per_mm[X_AXIS];
  pos[Y_AXIS] = (float)planner_position_steps[Y_AXIS] / planner_settings.steps_per_mm[Y_AXIS];
  pos[Z_AXIS] = (float)planner_position_steps[Z_AXIS] / planner_settings.steps_per_mm[Z_AXIS];
}


uint8_t plan_check_full_buffer(void)
{
  return block_buffer_tail == next_buffer_head;
}


uint8_t plan_is_buffer_empty(void)
{
  return block_buffer_head == block_buffer_tail;
}


plan_block_t *plan_get_current_block(void)
{
  if (plan_is_buffer_empty()) return 0;
  return &block_buffer[block_buffer_tail];
}


void plan_discard_current_block(void)
{
  if (!plan_is_buffer_empty()) {
    block_buffer_tail = plan_next_block_index(block_buffer_tail);
  }
}


static float limit_rate_by_axis(float *unit_vec)
{
  float rate = 1000000.0f;

  for (uint8_t i = 0; i < N_AXIS; i++) {
    float u = fabsf(unit_vec[i]);
    if (u > 0.000001f) {
      float r = planner_settings.max_rate[i] / u;
      if (r < rate) rate = r;
    }
  }

  return rate;
}


static float limit_accel_by_axis(float *unit_vec)
{
  float accel = 1000000.0f;

  for (uint8_t i = 0; i < N_AXIS; i++) {
    float u = fabsf(unit_vec[i]);
    if (u > 0.000001f) {
      float a = planner_settings.acceleration[i] / u;
      if (a < accel) accel = a;
    }
  }

  return accel;
}

/*
  Simplified GRBL-like junction speed:
  - straight line: can keep speed
  - reverse or sharp corner: slow down
  - uses junction deviation to limit cornering speed
*/
static float compute_junction_speed(plan_block_t *block)
{
	//xet block dau tien
  if (previous_nominal_speed <= 0.0f) return 0.0f;

	//tinh goc giua 2 block - dung tich vo huong
  float cos_theta = 0.0f;
  for (uint8_t i = 0; i < N_AXIS; i++) {
    cos_theta -= previous_unit_vec[i] * block->unit_vec[i];
  }

	//goc = 180 nguoc chieu
  if (cos_theta > 0.999999f) return 0.0f; // reversal
	//goc = 0 cung chieu
  if (cos_theta < -0.999999f) return fminf(previous_nominal_speed, block->nominal_speed); // straight

	//goc cua, tinh toc do cua cho phep
  float sin_theta_d2 = sqrtf(0.5f * (1.0f - cos_theta));
  float limit = sqrtf((block->acceleration * planner_settings.junction_deviation * sin_theta_d2) /
                      (1.0f - sin_theta_d2));

  return fminf(limit, fminf(previous_nominal_speed, block->nominal_speed));
}


static void planner_recalculate(void)
{
  if (plan_is_buffer_empty()) return;

  uint8_t block_index = plan_prev_block_index(block_buffer_head);
  plan_block_t *current = &block_buffer[block_index];

  // Newest block must be able to stop at end.
  current->entry_speed = fminf(current->entry_speed, current->max_entry_speed);
  current->exit_speed = 0.0f;

  // Reverse pass: limit entry speeds so every block can decelerate to next block.
  //v² = u² + 2as
  /*
  u - allowable = t?c ð? vào l?n nh?t cho phép c?a block trý?c
  v - current->entry_speed = t?c ð? c?n ð?t ? block sau
  a - prev->acceleration = gia t?c/gi?m t?c c?a block trý?c
  s - prev->millimeters = chi?u dài block trý?c
  */
  while (block_index != block_buffer_tail) {
    uint8_t prev_index = plan_prev_block_index(block_index);
    plan_block_t *prev = &block_buffer[prev_index];

    float allowable = sqrtf(current->entry_speed * current->entry_speed +
                            2.0f * prev->acceleration * prev->millimeters);

    if (prev->entry_speed > allowable) prev->entry_speed = allowable;
    if (prev->entry_speed > prev->max_entry_speed) prev->entry_speed = prev->max_entry_speed;

    current = prev;
    block_index = prev_index;
  }

  // Forward pass: limit acceleration from previous block.
  block_index = block_buffer_tail;
  current = &block_buffer[block_index];

  while (plan_next_block_index(block_index) != block_buffer_head) {
    uint8_t next_index = plan_next_block_index(block_index);
    plan_block_t *next = &block_buffer[next_index];

    float allowable = sqrtf(current->entry_speed * current->entry_speed +
                            2.0f * current->acceleration * current->millimeters);

    if (next->entry_speed > allowable) next->entry_speed = allowable;

    current = next;
    block_index = next_index;
  }

  // Set exit speed of each block as entry speed of next block.
  block_index = block_buffer_tail;
  while (block_index != block_buffer_head) {
    uint8_t next_index = plan_next_block_index(block_index);
    if (next_index == block_buffer_head) {
      block_buffer[block_index].exit_speed = 0.0f;
      break;
    } else {
      block_buffer[block_index].exit_speed = block_buffer[next_index].entry_speed;
    }
    block_index = next_index;
  }
}


// nhan toa do dich va bien thanh block chuyen dong
uint8_t plan_buffer_line(float *target, plan_line_data_t *pl_data)
{
	//1. ktra buffer
  if (plan_check_full_buffer()) return PLAN_EMPTY_BLOCK;

	//2. doi toa do tu mm -> step
  int32_t target_steps[N_AXIS];

  target_steps[X_AXIS] = lroundf(target[X_AXIS] * planner_settings.steps_per_mm[X_AXIS]);
  target_steps[Y_AXIS] = lroundf(target[Y_AXIS] * planner_settings.steps_per_mm[Y_AXIS]);
  target_steps[Z_AXIS] = lroundf(target[Z_AXIS] * planner_settings.steps_per_mm[Z_AXIS]);

	//3. tinh delta step va mm
  int32_t delta_steps[N_AXIS];
  float delta_mm[N_AXIS];
  uint32_t max_steps = 0;

  plan_block_t *block = &block_buffer[block_buffer_head];
  memset(block, 0, sizeof(plan_block_t));

  for (uint8_t i = 0; i < N_AXIS; i++) {
    delta_steps[i] = target_steps[i] - planner_position_steps[i];
    block->steps[i] = labs(delta_steps[i]);

    if (block->steps[i] > max_steps) max_steps = block->steps[i];

    delta_mm[i] = (float)delta_steps[i] / planner_settings.steps_per_mm[i];
  }
  
	//4. tim truc co nhieu step nhat
  if (max_steps == 0) return PLAN_EMPTY_BLOCK;

  block->step_event_count = max_steps;

	//5. tinh chieu quay
  if (delta_steps[X_AXIS] < 0) block->direction_bits |= (1 << X_DIRECTION_BIT);
  if (delta_steps[Y_AXIS] < 0) block->direction_bits |= (1 << Y_DIRECTION_BIT);
  if (delta_steps[Z_AXIS] < 0) block->direction_bits |= (1 << Z_DIRECTION_BIT);

	//6. tinh chieu dai duong di
  block->millimeters = sqrtf(delta_mm[X_AXIS] * delta_mm[X_AXIS] +
                             delta_mm[Y_AXIS] * delta_mm[Y_AXIS] +
                             delta_mm[Z_AXIS] * delta_mm[Z_AXIS]);

  if (block->millimeters <= 0.0f) return PLAN_EMPTY_BLOCK;

	//7. tinh vector huong di
  for (uint8_t i = 0; i < N_AXIS; i++) {
    block->unit_vec[i] = delta_mm[i] / block->millimeters;
  }

	//8. tinh toc do chay
  block->rapid_rate = limit_rate_by_axis(block->unit_vec);
  block->programmed_rate = pl_data->rapid_motion ? block->rapid_rate : pl_data->feed_rate;

  if (block->programmed_rate > block->rapid_rate) {
    block->programmed_rate = block->rapid_rate;
  }

  if (block->programmed_rate < 1.0f) {
    block->programmed_rate = 1.0f;
  }

	//9. doi toc do tu mm/min -> mm/sec
  block->nominal_speed = block->programmed_rate / 60.0f;
  
	//10. tinh gia toc cua block
  block->acceleration = limit_accel_by_axis(block->unit_vec);

	//11. tinh toc do vao block
  block->max_entry_speed = compute_junction_speed(block);
  block->entry_speed = block->max_entry_speed;
  block->exit_speed = 0.0f;

	//12. luu block hien tai thanh block truoc
  memcpy(previous_unit_vec, block->unit_vec, sizeof(previous_unit_vec));
  previous_nominal_speed = block->nominal_speed;

	//13. cap nhat vi tri planner
  for (uint8_t i = 0; i < N_AXIS; i++) {
    planner_position_steps[i] = target_steps[i];
  }

	//14. day head buffer sang o tiep theo
  block_buffer_head = next_buffer_head;
  next_buffer_head = plan_next_block_index(block_buffer_head);

	//15. planner tinh lai toc do vao/ra cua cac block
  planner_recalculate();

  return PLAN_OK;
}
