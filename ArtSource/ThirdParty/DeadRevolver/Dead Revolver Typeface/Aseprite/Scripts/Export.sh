#!/bin/bash

ASEPRITE_PATH="C:\Program Files (x86)\Steam\steamapps\common\Aseprite\Aseprite.exe"

GAME_PATH="./../Game.aseprite"
GAME_COMPACT_PATH="./../GameCompact.aseprite"
DISPLAY_PATH="./../Display.aseprite"
DIGITAL_PATH="./../Digital.aseprite"
ARCADE_PATH="./../Arcade.aseprite"
EXPORT_FOLDER_TAG_COMBINATIONS="./ExportFonts.lua"
SPRITES_FOLDER="./../../Fonts/"
PARAMS="--script-param sprites-folder=$SPRITES_FOLDER"

display_menu() {
  echo "Please choose an option:"
  echo "1. Game"
  echo "2. Arcade"
  echo "3. Display"
  echo "4. Digital"
  echo "5. All"
  echo "6. Exit"
}

export_game() {
  echo "Exporting Game Font"
  "$ASEPRITE_PATH" -b "$GAME_PATH" $PARAMS --script "$EXPORT_FOLDER_TAG_COMBINATIONS"
  "$ASEPRITE_PATH" -b "$GAME_COMPACT_PATH" $PARAMS --script "$EXPORT_FOLDER_TAG_COMBINATIONS"
}

export_arcade() {
  echo "Exporting Arcade Font"
  "$ASEPRITE_PATH" -b "$ARCADE_PATH" $PARAMS --script "$EXPORT_FOLDER_TAG_COMBINATIONS"
}

export_display() {
  echo "Exporting Display Font"
      "$ASEPRITE_PATH" -b "$DISPLAY_PATH" $PARAMS --script "$EXPORT_FOLDER_TAG_COMBINATIONS"
}

export_digital() {
  echo "Exporting Digital Font"
      "$ASEPRITE_PATH" -b "$DIGITAL_PATH" $PARAMS --script "$EXPORT_FOLDER_TAG_COMBINATIONS"
}

while true; do
  display_menu
  read -p "Enter your choice [1-6]: " choice

  case $choice in
    1)
      export_game
      ;;
    2)
      export_arcade
      ;;
    3)
      export_display
      ;;
    4)
      export_digital
      ;;
    5)
      export_game
      export_arcade
      export_display
      export_digital
      ;;
    6)
      echo "Exiting..."
      break
      ;;
    *)
      echo "Invalid option. Please try again."
      ;;
  esac

  echo ""
done