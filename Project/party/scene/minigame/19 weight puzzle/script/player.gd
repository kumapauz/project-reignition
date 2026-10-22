### Represents a player in the Weight Puzzle minigame.
extends PartyGameCharacterSpawner

@export var camera: Camera3D
@export var cursor: Control
@export var coin_box: Node3D
@export var coin_meshes: Array[Node3D]
@export var coin_particles: GPUParticles3D
@export var coin_particles_player : GPUParticles3D
@export var weight_platform2: Node3D
@export var coinbox_offset: Node3D
@export var weight_animator: AnimationPlayer
@export var hand_attachment: BoneAttachment3D
@export var debug_label: Label3D
@export var weight_label: Label
@export var coin_reduction_timer: Timer

var zeroout_hands: bool = false

var is_demo_complete: bool
##Can the player tilt the bucket
var can_pour : bool
##The current number of coins in the bucket
var num_coins: int
var current_input: float

const ANIM_LIST : PackedStringArray = [
	"19-catch",
	"19-lift-wait",
	"19-miss",
	"19-return",
	"19-slant2-wait",
	"wait"
]

const COINS_TO_START: int = 100
## The amount of coins needed to win the minigame
const COINS_TO_REACH: int = 50
const COINS_TIMER_SPEED_SOFT : float = 0.3
const COINS_TIMER_SPEED_HARD : float = 0.1
## The amount of coin particles to pour out
const PARTICLE_SOFT: int = 12
const PARTICLE_HARD: int = 20

const TILT_BLEND : StringName = "parameters/tilt-blend/blend_amount"
const HOLD_TRANSITION : StringName = "parameters/hold-transition/transition_request"
const ONESHOT_TRIGGER : StringName = "parameters/oneshot-trigger/request"
const ONESHOT_TRANSITION : StringName = "parameters/oneshot-transition/transition_request"
const HOLD_ANIM : StringName = "hold"
const WAIT_ANIM : StringName = "wait"
const RETURN_ANIM : StringName = "return"
const MISS_ANIM : StringName = "miss"
const CATCH_ANIM : StringName = "catch"

func on_spawn_finished() -> void:
	super()
	rollback_timer.register_target(self)
	initialize_animation_tree(get_anim_prefix(), ANIM_LIST)
	
	num_coins = 0
	hand_attachment.reparent(character_animator.skeleton)
	MinigameManager.instance.gameplay_started.connect(Callable(self, "send_chest"))
	animation_tree.set(HOLD_TRANSITION, WAIT_ANIM)

	cursor.position = camera.get_viewport().get_visible_rect().size / 2

	if player_index == 0:
		set_coins_stage(5)
	else:
		set_coins_stage(0)

func on_minigame_finished() -> void:
	coin_box.visible = false

func _physics_process(_delta: float) -> void:
	if is_demo_complete:
		if can_pour:
			process_inputs()
			
			if current_input > 0:
			
				if current_input > 0.5 && coin_particles_player.amount != PARTICLE_HARD:
					coin_particles_player.amount = PARTICLE_HARD
					coin_reduction_timer.wait_time = COINS_TIMER_SPEED_HARD
				elif current_input <= 0.5 && coin_particles_player.amount != PARTICLE_SOFT:
					coin_particles_player.amount = PARTICLE_SOFT
					coin_reduction_timer.wait_time = COINS_TIMER_SPEED_SOFT

				if coin_particles_player.emitting != true && num_coins != 0:
					pour_coins_visual(true)

				if coin_reduction_timer.is_stopped():
					coin_reduction_timer.start()

				cpu_check_coins()

			else:
				coin_reduction_timer.stop()
				if coin_particles_player.emitting != false:
					pour_coins_visual(false)
		else:
			current_input = 0.0

func process_inputs() -> void:
	current_input = max(get_vertical_input(), 0)

	if is_cpu():
		current_input = 0.85
		print("Current Input: " + str(current_input))
	if !is_cpu() && Input.is_action_just_pressed("button_primary%s" % get_input_suffix()):
		throw_chest() # TODO Make this work with netplay

func _process(_delta: float) -> void:
	debug_label.text = str(num_coins)
	if zeroout_hands:
		zero_hands()
	process_animation()
	
func complete_demo() -> void:
	current_input = 0
	is_demo_complete = true
	set_physics_process(true)

	if is_minigame_host():
		var start_callable : Callable = Callable.create(MinigameManager.instance, "request_minigame_start")
		get_tree().create_timer(1).timeout.connect(start_callable)

##Have the player catch the chest
func catch_chest() -> void:
	set_zero(true)
	coin_box.reparent(hand_attachment)
	play_oneshot_animation(CATCH_ANIM)
	animation_tree.set(HOLD_TRANSITION, HOLD_ANIM)
	await get_tree().create_timer(1.2).timeout
	set_can_pour(true)

##Have the player return the chest to the weight
func throw_chest() -> void:
	var weight_num : int

	weight_num = num_coins - 50

	if weight_num > 0:
		weight_label.text = "+" + str(weight_num) + "minigame_19_weight"
	else:
		weight_label.text = str(weight_num) + "minigame_19_weight"



	cpu_num_mistakes -= 1
	cpu_num_mistakes = clampi(cpu_num_mistakes, 0, 5)
	cpu_choose_coins()

	coin_particles_player.emitting = false
	weight_animator.play("weight_return")
	set_can_pour(false)
	play_oneshot_animation(RETURN_ANIM)
	animation_tree.set(HOLD_TRANSITION, WAIT_ANIM)

##Send the chest from the weight to the player
func send_chest() -> void:
	
	if num_coins < COINS_TO_REACH:
		coin_particles.emitting = true

		await get_tree().create_timer(0.5).timeout

		if num_coins == 0:
			await get_tree().create_timer(3).timeout
		elif num_coins > 0 && num_coins <= 25:
			await get_tree().create_timer(2).timeout
		elif num_coins > 25 && num_coins <= 40:
			await get_tree().create_timer(1).timeout
		elif num_coins > 40 && num_coins <= 50:
			await get_tree().create_timer(0.5).timeout
		
		set_coins_stage(5)
	
		coin_particles.emitting = false
		num_coins = COINS_TO_START
	weight_animator.play("weight_toss")

##Checks if the player has the right amount of weight
func check_win_condition() -> void:
	if is_demo_complete:
		weight_animator.play(get_bucket_weight_anim())

##Unparents from the player and re-parents to the weight
func unparent() -> void:
	print("Unparenting")
	coin_box.reparent(coinbox_offset)

	
func zero_hands() -> void:
	coin_box.position = Vector3.ZERO
	coin_box.rotation = Vector3.ZERO

func set_zero(zero: bool) -> void:
	zeroout_hands = zero

# Tilts the player in the introduction
func intro_tilt() -> void:
	current_input = 0.85

func play_oneshot_animation(anim: StringName):
	animation_tree.set(ONESHOT_TRANSITION, anim)
	animation_tree.set(ONESHOT_TRIGGER, AnimationNodeOneShot.ONE_SHOT_REQUEST_FIRE)

func set_can_pour(pour: bool):
	print("setting pour")
	can_pour = pour

func pour_coins_visual(pour: bool) -> void:
	if pour:
		coin_particles_player.emitting = true
	else:
		coin_particles_player.emitting = false

func set_coins_stage(stage: int) -> void:
	for i in 5:
		coin_meshes[i].visible = false

	if stage != 0:
		for i in stage:
			coin_meshes[i].visible = true

##Returns the proper coin stage for the number of coins the player has
func check_coins_stage() -> int:
	
	if num_coins == 0:
		return 0
	elif num_coins > 0 && num_coins < 20:
		return 1
	elif num_coins >= 20 && num_coins < 40:
		return 2
	elif num_coins >= 40 && num_coins < 60:
		return 3
	elif num_coins >= 60 && num_coins < 80:
		return 4
	
	return 5

##Determines which weight animation should play
func get_bucket_weight_anim() -> String:

	if num_coins <= 15:
		return "check_pos_0"
	elif num_coins > 15 && num_coins <= 25:
		return "check_pos_1"
	elif num_coins > 25 && num_coins <= 35:
		return "check_pos_2"
	elif num_coins > 35 && num_coins <= 45:
		return "check_pos_3"
	elif num_coins > 45 && num_coins <= 55:
		return "check_pos_4"
	elif num_coins > 55 && num_coins <= 65:
		return "check_pos_5"
	elif num_coins > 65 && num_coins <= 75:
		return "check_pos_6"
	elif num_coins > 75 && num_coins <= 85:
		return "check_pos_7"
	elif num_coins > 85 && num_coins <= 95:
		return "check_pos_8"
	
	
	return "check_pos_9"


func _on_coin_reduction_timer_timeout() -> void:
	num_coins -= 1
	num_coins = clampi(num_coins, 0, 100)
	set_coins_stage(check_coins_stage())

	if num_coins == 0:
		coin_particles_player.emitting = false

	coin_reduction_timer.start()

func finish_minigame() -> void:
	if is_demo_complete:
		MinigameManager.instance.request_score_change(player_index, 1)
		MinigameManager.instance.request_minigame_finish()
	
func process_animation() -> void:
	animation_tree.set(TILT_BLEND, current_input)

#####################
### ROLLBACK CODE ###
#####################
@export var rollback_timer : RollbackTimer
const RB_INPUT : int = 0
func on_rollback_applied(rb_params : Array) -> void:
	current_input = rb_params[RB_INPUT]

func process_rollback() -> void:
	rollback_timer.set_param(RB_INPUT, current_input)
	rollback_timer.process_rollback()

################
### CPU CODE ###
################

var cpu_num_mistakes : int
var cpu_num_coins : int

##How many mistakes can a CPU make before getting the right answer?
func cpu_decide_mistakes() -> int:
	match get_cpu_difficulty():
		PlayerData.CPU_DIFFICULTY_ENUM.EASY:
			return 4
		PlayerData.CPU_DIFFICULTY_ENUM.NORMAL:
			return 3
		PlayerData.CPU_DIFFICULTY_ENUM.HARD:
			return 2
		PlayerData.CPU_DIFFICULTY_ENUM.EXTREME:
			return 1
	return 0


func cpu_check_coins() -> void:
	if is_cpu:
		if num_coins == cpu_num_coins:
			throw_chest()


func cpu_choose_coins() -> int:
	if cpu_num_mistakes == 0:
		return 45
	
	return randi_range(0,100)

func cpu_tilt() -> void:
	print("CPU POURING")
	current_input = 0.85




