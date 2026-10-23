local sprite = app.activeSprite
assert(sprite ~= nil, "Open the character animation before running this script")
assert(#sprite.frames == 12, "Expected 12 animation frames")

local idle = sprite:newTag(1, 6)
idle.name = "Idle"

local run = sprite:newTag(7, 12)
run.name = "Run"

app.command.SaveFileAs {
  filename = app.params["out"],
}
