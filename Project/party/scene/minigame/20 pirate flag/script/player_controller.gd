extends PartyGameCharacterSpawner

@export var hand_attachment: BoneAttachment3D


func on_spawn_finished() -> void:
	hand_attachment.reparent(character_animator.skeleton)
	character_animator.play_animation("%s/wait" % MinigameManager.ANIMATION_LIBRARY_PREFIX, true)


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass
