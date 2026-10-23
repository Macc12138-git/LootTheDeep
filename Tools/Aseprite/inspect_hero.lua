local sprite = app.activeSprite
print("ASEPRITE_SCRIPT_OK")
print("frames=" .. #sprite.frames .. " layers=" .. #sprite.layers)
print("out=" .. tostring(app.params["out"]))
