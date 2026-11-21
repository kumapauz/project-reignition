class_name FlagPlayerController extends PartyGameCharacterSpawner

@export var hand_attachment: BoneAttachment3D

##Is the player currently waving the flag?
var flag_active: bool
##Controls the flag movement
var flagBlend: Vector2
##How long should it take to complete a single flag wave
const FLAGWAVETIME: float = 0.15

func on_spawn_finished() -> void:
	hand_attachment.reparent(character_animator.skeleton)
	animation_tree.anim_player = character_animator.animator.get_path()
	flagBlend = Vector2.ZERO
	set_physics_process(true)
	
func process_animation() -> void:
	animation_tree.set("parameters/blend_position", flagBlend)

func process_inputs() -> void:
	if !is_cpu() && !flag_active:
		if Input.is_action_just_pressed("move_left%s" % get_input_suffix()):
			flag_left()
		elif Input.is_action_just_pressed("move_right%s" % get_input_suffix()):
			flag_right()
		elif Input.is_action_just_pressed("move_up%s" % get_input_suffix()):
			flag_up()
		elif Input.is_action_just_pressed("move_down%s" % get_input_suffix()):
			flag_down()



func _physics_process(delta: float) -> void:
	process_inputs()
	process_animation()
	

# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass


func flag_left() -> void:
	print("flag left")
	var tween = get_tree().create_tween()
	tween.tween_property(self, "flagBlend", Vector2.LEFT,FLAGWAVETIME)
	flag_active = true

func flag_right() -> void:
	print("flag right")
	var tween = get_tree().create_tween()
	tween.tween_property(self, "flagBlend", Vector2.RIGHT,FLAGWAVETIME)
	flag_active = true

func flag_up() -> void:
	print("flag up")
	var tween = get_tree().create_tween()
	tween.tween_property(self, "flagBlend", Vector2.UP,FLAGWAVETIME)
	flag_active = true

func flag_down() -> void:
	print("flag down")
	var tween = get_tree().create_tween()
	tween.tween_property(self, "flagBlend", Vector2.DOWN,FLAGWAVETIME)
	flag_active = true
	

func flag_return() -> void:
	var tween = get_tree().create_tween()
	tween.tween_property(self, "flagBlend", Vector2.ZERO,FLAGWAVETIME)
	flag_active = false
