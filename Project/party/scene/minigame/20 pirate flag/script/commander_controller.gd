class_name FlagCommanderController extends Node3D

@export var majin: CharacterAnimator
@export var hand_attachment: BoneAttachment3D
@export var anim_tree: AnimationTree

var flagBlend: Vector2

##How long should it take to complete a single flag wave (Waving and returning to center)
const FLAGWAVETIME: float = 0.5
##How long should it take to wait between waving and returing to the center
const FLAGWAVEWAITTIME: float = 0.25

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	flagBlend = Vector2.ZERO
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	process_animation()
	pass

func process_animation() -> void:
	anim_tree.set("parameters/FlagWave/blend_position", flagBlend)


func flag_left() -> void:
	
	#TODO: Play whistle SFX here 
	await get_tree().create_timer(FLAGWAVETIME).timeout

	var tween = get_tree().create_tween()
	tween.tween_property(self, "flagBlend", Vector2.LEFT,FLAGWAVETIME / 2)
	tween.tween_interval(FLAGWAVEWAITTIME)
	tween.tween_property(self, "flagBlend", Vector2.ZERO,FLAGWAVETIME / 2)

func flag_right() -> void:
	#TODO: Play whistle SFX here
	await get_tree().create_timer(FLAGWAVETIME).timeout

	var tween = get_tree().create_tween()
	tween.tween_property(self, "flagBlend", Vector2.RIGHT,FLAGWAVETIME / 2)
	tween.tween_interval(FLAGWAVEWAITTIME)
	tween.tween_property(self, "flagBlend", Vector2.ZERO,FLAGWAVETIME / 2)

func flag_up() -> void:
	#TODO: Play whistle SFX here 
	await get_tree().create_timer(FLAGWAVETIME).timeout

	var tween = get_tree().create_tween()
	tween.tween_property(self, "flagBlend", Vector2.UP,FLAGWAVETIME / 2)
	tween.tween_interval(FLAGWAVEWAITTIME)
	tween.tween_property(self, "flagBlend", Vector2.ZERO,FLAGWAVETIME / 2)

func flag_down() -> void:
	#TODO: Play whistle SFX here 
	await get_tree().create_timer(FLAGWAVETIME).timeout

	var tween = get_tree().create_tween()
	tween.tween_property(self, "flagBlend", Vector2.DOWN,FLAGWAVETIME / 2)
	tween.tween_interval(FLAGWAVEWAITTIME)
	tween.tween_property(self, "flagBlend", Vector2.ZERO,FLAGWAVETIME / 2)
