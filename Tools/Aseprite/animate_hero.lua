-- Animate the user's existing Hero pixels; do not redraw the character.
local source = app.activeSprite
assert(source and source.width == 64 and source.height == 64,
       "Expected the user's 64x64 Hero sprite")
assert(#source.frames == 1 and #source.layers == 1,
       "Expected the original single-frame Hero file")
local output = assert(app.params["out"], "Pass --script-param out=...")
local original = Image(source.spec)
original:drawImage(source.cels[1].image, source.cels[1].position)

local animation = Sprite(source.width, source.height, ColorMode.RGB)
animation:setPalette(source.palettes[1])
local layerNames = {
  "Feet - far", "Feet - near", "Body", "Scarf - tail",
  "Head and goggles", "Scarf - neck",
}
local layers = {}
layers[1] = animation.layers[1]
layers[1].name = layerNames[1]
for i = 2, #layerNames do
  layers[i] = animation:newLayer()
  layers[i].name = layerNames[i]
end

local parts = {}
for i = 1, #layers do parts[i] = {} end
for y = 0, source.height - 1 do
  for x = 0, source.width - 1 do
    local color = original:getPixel(x, y)
    if app.pixelColor.rgbaA(color) > 0 then
      local part
      if y <= 34 then
        part = 5
      elseif x <= 27 then
        part = 4
      elseif y <= 37 then
        part = 6
      elseif y <= 42 then
        part = 3
      elseif x < 36 then
        part = 1
      else
        part = 2
      end
      table.insert(parts[part], {x=x, y=y, color=color})
    end
  end
end

local idle = {
  {dy=0, tip=0, duration=0.24},
  {dy=0, tip=1, duration=0.16},
  {dy=-1, tip=1, duration=0.20},
  {dy=-1, tip=0, duration=0.24},
  {dy=0, tip=-1, duration=0.18},
  {dy=0, tip=0, duration=0.18},
}
-- Contact, recoil, passing, flight, opposite contact, recoil, passing, flight.
-- Feet retain the original tiny black pixel shapes throughout the cycle.
local move = {
  {dy=0,  tip=-1, far={-2,0}, near={2,0}, duration=0.10},
  {dy=1,  tip=0,  far={-1,-1}, near={1,0}, duration=0.08},
  {dy=0,  tip=1,  far={1,-2}, near={-1,0}, duration=0.08},
  {dy=-1, tip=0,  far={2,-2}, near={-2,-1}, duration=0.08},
  {dy=0,  tip=-1, far={2,0}, near={-2,0}, duration=0.10},
  {dy=1,  tip=0,  far={1,0}, near={-1,-1}, duration=0.08},
  {dy=0,  tip=1,  far={-1,0}, near={1,-2}, duration=0.08},
  {dy=-1, tip=0,  far={-2,-1}, near={2,-2}, duration=0.08},
}

local function round(value)
  return math.floor(value + 0.5)
end

local function paintFrame(frameNumber, settings, moving)
  local frame = frameNumber == 1 and animation.frames[1] or animation:newEmptyFrame()
  frame.duration = settings.duration
  local bodyShift = moving and settings.dy or 0
  for part, pixels in ipairs(parts) do
    local image = Image(animation.spec)
    for _, p in ipairs(pixels) do
      local x, y = p.x, p.y
      if part == 1 or part == 2 then
        if moving then
          local delta = part == 1 and settings.far or settings.near
          x, y = x + delta[1], y + delta[2]
        end
      elseif part == 3 then
        y = y + bodyShift
      elseif part == 4 then
        -- Keep the attachment fixed to the neck; bend progressively toward the tip.
        local weight = math.max(0, math.min(1, (27 - x) / 11))
        y = y + settings.dy + round(settings.tip * weight)
      else
        y = y + settings.dy
      end
      image:drawPixel(x, y, p.color)
    end
    if part == 3 and not moving and settings.dy == -1 then
      -- One repeated shoulder row bridges the breathing offset, with feet grounded.
      for _, p in ipairs(pixels) do
        if p.y == 38 then image:drawPixel(p.x, 37, p.color) end
      end
    end
    animation:newCel(layers[part], frame, image, Point(0, 0))
  end
end

app.transaction("Hero idle and movement", function()
  local frameNumber = 0
  for _, settings in ipairs(idle) do
    frameNumber = frameNumber + 1
    paintFrame(frameNumber, settings, false)
  end
  for _, settings in ipairs(move) do
    frameNumber = frameNumber + 1
    paintFrame(frameNumber, settings, true)
  end
  local idleTag = animation:newTag(1, #idle)
  idleTag.name = "Idle"
  idleTag.color = Color {r=96, g=175, b=220}
  local moveTag = animation:newTag(#idle + 1, #idle + #move)
  moveTag.name = "Move"
  moveTag.color = Color {r=239, g=149, b=64}
end)

-- The neutral animation frame must retain every original pixel.
local neutral = Image(animation.spec)
for _, layer in ipairs(layers) do
  local cel = layer:cel(1)
  neutral:drawImage(cel.image, cel.position)
end
for y = 0, source.height - 1 do
  for x = 0, source.width - 1 do
    assert(neutral:getPixel(x, y) == original:getPixel(x, y),
           "The neutral frame changed an original pixel")
  end
end

assert(animation:saveAs(output), "Could not save the animation")
print("Saved " .. output)
print("14 frames; Idle 1-6; Move 7-14; 6 editable layers")
