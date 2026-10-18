### Represents a player in the Weight Puzzle minigame.
extends PartyGameCharacterSpawner

@export var coin_box: Node3D
@export var coin_meshes: Array[Node3D]
@export var coin_particles: Array[GPUParticles3D]
@export var weight_platform2: Node3D
@export var coinbox_offset: Node3D
@export var weight_animator: AnimationPlayer
@export var hand_attachment: BoneAttachment3D
@export var debug_label: Label3D

var zeroout_hands: bool = false

var is_demo_complete: bool
##Can the player tilt the bucket
var can_pour : bool
##The current number of coins in the bucket
var num_coins: int
##A visual representation of how many coins are in the bucket
var num_coins_visual: int
var current_input: float
var tween : Tween

const ANIM_LIST : PackedStringArray = [
	"19-catch",
	"19-lift-wait",
	"19-miss",
	"19-return",
	"19-slant2-wait",
	"wait"
]

## The amount of coins needed to win the minigame
const COINS_TO_REACH: int = 50

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
	num_coins_visual = 0
	hand_attachment.reparent(character_animator.skeleton)
	MinigameManager.instance.gameplay_started.connect(Callable(self, "send_chest"))
	print("WAITING!")
	animation_tree.set(HOLD_TRANSITION, WAIT_ANIM)

func _physics_process(_delta: float) -> void:
	if is_demo_complete:
		if can_pour:
			process_inputs()
		else:
			current_input = 0.0

func process_inputs() -> void:
	current_input = max(get_vertical_input(), 0)
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

#Have the player catch the chest
func catch_chest() -> void:
	set_zero(true)
	coin_box.reparent(hand_attachment)
	play_oneshot_animation(CATCH_ANIM)
	print("HOLDING!")
	animation_tree.set(HOLD_TRANSITION, HOLD_ANIM)
	await get_tree().create_timer(1.2).timeout
	set_can_pour(true)

#Have the player return the chest to the weight
func throw_chest() -> void:
	weight_animator.play("weight_return")
	set_can_pour(false)
	play_oneshot_animation(RETURN_ANIM)
	print("WAITING!")
	animation_tree.set(HOLD_TRANSITION, WAIT_ANIM)

#Send the chest from the weight to the player
func send_chest() -> void:
	
	if num_coins < COINS_TO_REACH:
		coin_particles[0].emitting = true
		coin_particles[1].emitting = true
		coin_particles[2].emitting = true

		await get_tree().create_timer(0.5).timeout

		if num_coins == 0:
			await get_tree().create_timer(3).timeout
		elif num_coins > 0 && num_coins <= 25:
			await get_tree().create_timer(2).timeout
		elif num_coins > 25 && num_coins <= 40:
			await get_tree().create_timer(1).timeout
		elif num_coins > 40 && num_coins <= 50:
			await get_tree().create_timer(0.5).timeout
	
		coin_particles[0].emitting = false
		coin_particles[1].emitting = false
		coin_particles[2].emitting = false
		num_coins = 100
	weight_animator.play("weight_toss")

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

func process_animation() -> void:
	animation_tree.set(TILT_BLEND, current_input)
