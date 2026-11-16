extends Node3D

@export var majin: CharacterAnimator
@export var hand_attachment: BoneAttachment3D
	

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	majin.animator.play("%s/" % MinigameManager.ANIMATION_LIBRARY_PREFIX + "wait", true)
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass
