local sprite = app.activeSprite
local trim = app.params["trim"]
local trimCels = app.params["trim-cels"]
local spritesFolder = app.params["sprites-folder"]
print(spritesFolder)

if not spritesFolder then
  spritesFolder = "Sprites/"
end

if not trim then
  trim = true
end

if trim == "false" then
  trim = false
end

if trimCels == "true" then
  trimCels = true
end

local spriteName = app.fs.fileTitle(sprite.filename);

if not sprite then
  return app.alert("No active sprite")
end

local baseFolder = spritesFolder .. spriteName .. "/Bitmaps/"
local baseFilename = baseFolder

local sliceCount = {}

for _, slice in ipairs(app.activeSprite.slices) do
    local sliceId = slice.data .. "/" .. slice.name
    sliceCount[sliceId] = (sliceCount[sliceId] or 0) + 1
end

for _, group in ipairs(sprite.layers) do
  if group.isGroup then

    -- for _, tag in ipairs(sprite.tags) do
    -- local sanitizedTagName = tag.name:gsub("[^%w_]", "")
    local outputFilename = app.fs.joinPath(baseFilename, spriteName .. group.name .. ".png")
    -- local outputFilename = baseFilenameapp.fs.joinPath(baseFilename, spriteName .. group.name .. ".png")
    print(outputFilename)

    for _, slice in ipairs(app.activeSprite.slices) do
      app.activeSprite:crop(slice.bounds)
  
      local sliceId = slice.data .. "/" .. slice.name
      local sliceFilename = slice.name .. ".png"
  
      if sliceCount[sliceId] > 1 then
          sliceFilename = sliceFilename .. "_" .. sliceCount[sliceId]
      end
  
      -- local groupPath = slice.data:gsub("[^%w_]", "/")
      -- local fullOutputPath = app.fs.joinPath(outputPath, groupPath)
  
      -- app.fs.makeDirectory(outputFilename)
      -- app.activeSprite:saveCopyAs(app.fs.joinPath(outputFilename, spriteName .. group.name .. ".png"))
      app.command.ExportSpriteSheet {
        ui = false,
        askOverwrite = false,
        layer = group.name,
        type = SpriteSheetType.HORIZONTAL,
        textureFilename = outputFilename,
        splitTags = true,
      }
      sliceCount[sliceId] = sliceCount[sliceId] - 1
  end

    -- app.command.ExportSpriteSheet {
    --   ui = false,
    --   askOverwrite = false,
    --   layer = group.name,
    --   type = SpriteSheetType.HORIZONTAL,
    --   textureFilename = outputFilename,
    --   splitTags = true,
    --   trimSprite = true,
    --   innerPadding = 1
    -- }
    -- end
  end
end

local function has_groups(sprite)
  for _, layer in ipairs(sprite.layers) do
    if layer.isGroup then
      return true
    end
  end
  return false
end

if not has_groups(sprite) then

  -- for _, tag in ipairs(sprite.tags) do
    local outputFilename = app.fs.joinPath(baseFilename, app.fs.fileTitle(sprite.filename) .. ".png")

    app.command.ExportSpriteSheet {
      ui = false,
      askOverwrite = false,
      tag = tag.name,
      type = SpriteSheetType.HORIZONTAL,
      textureFilename = outputFilename,
      splitTags = true,
      trimSprite = trim
    }
  -- end
end