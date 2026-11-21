#This script controls the RNG for the flag, and controls the states of the players and commander
class_name FlagController extends Node

@export var commander: FlagCommanderController
@export var player1: FlagPlayerController
@export var player2: FlagPlayerController
@export var player3: FlagPlayerController
@export var player4: FlagPlayerController
@export var camera_anim: AnimationPlayer

##Which side is currently in control
enum ControllerState{
	COMMANDER,
	PLAYER
}

enum DIRECTION{
	UP,
	DOWN,
	LEFT,
	RIGHT
}

##The current flag sequence
var sequence: Array[DIRECTION]
##The current spot in the sequence during the player state
var player_placement: int
##How many flag waves should we generate
var sequence_amt: int

var rng: RandomNumberGenerator

##The amount of flag waves to start
const STARTING_AMT: int = 4
##How many flag waves to add to a new sequence
const SEQUENCE_ADD: int = 2

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	rng = RandomNumberGenerator.new()

	sequence_amt = STARTING_AMT
	create_sequence(sequence_amt)

	MinigameManager.instance.gameplay_started.connect(Callable(self, "play_sequence()"))
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass

##Creates a flag sequence
func create_sequence(amt: int) -> void:
	sequence.clear()
	print("Generated Sequence: ")
	for i in range(amt):
		sequence.append(rng.randi_range(0,3)) #Generates a random direction

		if i != 0:
			while sequence[i] == sequence[i - 1]: #Make sure the current direction isn't the same as the previous one
				sequence[i] = rng.randi_range(0,3) as DIRECTION
		print(str(sequence[i]))

	sequence_amt += SEQUENCE_ADD

func play_sequence() -> void:
	camera_anim.play("ZoomIn")
	await get_tree().create_timer(0.3).timeout
	
	for i in range(0, sequence.size()):
		play_flag_anim(sequence[i])

	await get_tree().create_timer(0.3).timeout
	camera_anim.play("ZoomOut")
	
func play_flag_anim(dir: DIRECTION) -> void:
	match dir:
		DIRECTION.UP:
			commander.flag_up()
		DIRECTION.DOWN:
			commander.flag_down()
		DIRECTION.LEFT:
			commander.flag_left()
		DIRECTION.RIGHT:
			commander.flag_right()
