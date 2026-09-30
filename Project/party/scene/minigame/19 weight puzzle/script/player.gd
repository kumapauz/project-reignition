### Represents a player in the Weight Puzzle minigame.
extends PartyGameCharacterSpawner

@export var coin_box: Node3D
@export var weight_platform2: Node3D
@export var coinbox_offset: Node3D
@export var weight_animator: AnimationPlayer
@export var hand_attachment: BoneAttachment3D
@export var debug_label: Label3D

var zeroout_hands: bool = false

##The current number of coins in the bucket
var num_coins: int

##The amount of coins needed to win the minigame
const COINS_TO_REACH: int = 50

func on_spawn_finished() -> void:
	super()

	#character_animator.play_animation("%s/19-lift-wait" % MinigameManager.ANIMATION_LIBRARY_PREFIX, true)
	character_animator.play_animation("wait")
	hand_attachment.reparent(character_animator.skeleton)
	demo_sequence()

func _process(delta: float) -> void:
	debug_label.text = str(num_coins)

	if zeroout_hands:
		zero_hands()
	
		
func catch_chest() -> void:
	set_zero(true)
	coin_box.reparent(hand_attachment)
	character_animator.play_animation("%s/19-catch" % MinigameManager.ANIMATION_LIBRARY_PREFIX, true)
	
	

func throw_chest() -> void:
	character_animator.play_animation("%s/19-return" % MinigameManager.ANIMATION_LIBRARY_PREFIX, true)

func unparent() -> void:
	print("Unparenting")
	coin_box.reparent(coinbox_offset)
	#coin_box.position = coinbox_offset.position
	

func demo_sequence() -> void:
	if player_index != 0:
		return
	
	character_animator.play_animation("wait")
	
	weight_animator.play("weight_toss")
	await get_tree().create_timer(3.5).timeout
	character_animator.play_animation("%s/19-slant1" % MinigameManager.ANIMATION_LIBRARY_PREFIX, true)
	##TODO: Emit coin particles when box is tilted
	await get_tree().create_timer(3).timeout
	weight_animator.play("weight_return")
	await get_tree().create_timer(1.1).timeout
	weight_animator.play("check_pos_4")
	##TODO: Put in circle animation from pitch black

func zero_hands() -> void:
	coin_box.position = Vector3.ZERO
	coin_box.rotation = Vector3.ZERO

func set_zero(zero: bool) -> void:
	zeroout_hands = zero
