; STM8S003F3 / STM8S103F3 register addresses (from ST's datasheets).

; ---- GPIO: ODR, IDR, DDR, CR1, CR2 ----
PA_ODR      equ $5000
PA_IDR      equ $5001
PA_DDR      equ $5002
PA_CR1      equ $5003
PA_CR2      equ $5004
PB_ODR      equ $5005
PB_IDR      equ $5006
PB_DDR      equ $5007
PB_CR1      equ $5008
PB_CR2      equ $5009
PC_ODR      equ $500A
PC_IDR      equ $500B
PC_DDR      equ $500C
PC_CR1      equ $500D
PC_CR2      equ $500E
PD_ODR      equ $500F
PD_IDR      equ $5010
PD_DDR      equ $5011
PD_CR1      equ $5012
PD_CR2      equ $5013

; ---- Flash and EEPROM programming ----
FLASH_CR1   equ $505A
FLASH_CR2   equ $505B
FLASH_NCR2  equ $505C
FLASH_IAPSR equ $505F
FLASH_PUKR  equ $5062
FLASH_DUKR  equ $5064

; ---- External interrupts ----
EXTI_CR1    equ $50A0
EXTI_CR2    equ $50A1

; ---- Clock ----
CLK_ICKR    equ $50C0
CLK_CKDIVR  equ $50C6
CLK_PCKENR1 equ $50C7
CLK_PCKENR2 equ $50CA

; ---- I2C ----
I2C_CR1     equ $5210
I2C_CR2     equ $5211
I2C_FREQR   equ $5212
I2C_OARL    equ $5213
I2C_OARH    equ $5214
I2C_DR      equ $5216
I2C_SR1     equ $5217
I2C_SR2     equ $5218
I2C_SR3     equ $5219
I2C_ITR     equ $521A
I2C_CCRL    equ $521B
I2C_CCRH    equ $521C
I2C_TRISER  equ $521D

; ---- TIM4 ----
TIM4_CR1    equ $5340
TIM4_IER    equ $5343
TIM4_SR     equ $5344
TIM4_EGR    equ $5345
TIM4_CNTR   equ $5346
TIM4_PSCR   equ $5347
TIM4_ARR    equ $5348

; ---- ADC1 ----
ADC_CSR     equ $5400
ADC_CR1     equ $5401
ADC_CR2     equ $5402
ADC_CR3     equ $5403
ADC_DRH     equ $5404
ADC_DRL     equ $5405
ADC_TDRH    equ $5406
ADC_TDRL    equ $5407

; ---- CPU and interrupt priorities ----
CFG_GCR     equ $7F60
ITC_SPR1    equ $7F70

; ---- Memory ----
RAM_END     equ $03FF
EEPROM      equ $4000

; ---- IRQ vectors: $8008 + 4 * IRQ ----
IRQ_EXTI_A  equ 3
IRQ_EXTI_D  equ 6
IRQ_I2C     equ 19
IRQ_TIM4    equ 23
