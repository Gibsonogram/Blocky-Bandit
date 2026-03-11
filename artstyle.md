# Artstyle Notes

I want to lean into a medieval-ruins style. Rooks, Bishops and other enemies will have a chess-like appearance, 
but alive, rook as if it's a living castle tower with window-eyes, bishop's head as if it's a knight's helm with 
a crack in it. (like the bishop divot)

Tone will be tough, but silly. Enemies look menacing, eyes light up on visibility, but they should also have 
a confused or dazed state when being moved by blocks.

## Basic tileset needs:
dirt, grass, basic plains with light discoloration between tiles to emphasize chess-board/tile locations. 
All gameplay should take place on these basic, plain tiles, no confusion about what the playable area is.
castle ruin walls and boundaries. These being made of a gray or otherwise more busy tileset would 
set a good distinction from the playable area.
Trees and other natural sprites to set around the map bounds. These can't be too complicated, 
as backgrounds may end up being randomized...

## Actors. 
I like the idea of the 'wooden' block/crate as the moveable item. Critical that I have lots of good idle 
anims on these, since most of the players time will be staring at the screen with nothing else to draw the eye.
Animations for movement must be really simple, maybe just exaggerate directional movement with angled knockback, 
little anim of movement trail to the player to telegraph exactly what happened.
When enemies fall or get squished, a short animation of scattering stones should play.
Animations for each move need to be quick, given rapid player input. I should exaggerate 1-2 frames, 
like a cartoonish leap, left-right-up-down when player moves.



## Art functionality notes:
Layers in 2D, for 3D top-down sensibility:
Split sprites into layers...

1. Ground
2. Walk in front of objs
3. Collision (on same plane)
3.5 PLAYER
4. Walk behind objs
5. Decoration

A shader or other mask should be created that falls between static underlying grid and the actors, 
to show the grid-lines in play.



## Assets, planning and ideas:
This is my first commercial game. Perhaps it's okay to be using assets that I buy from someone else.
The best solution here, might be to experiment with creation of 2D top-down assets with asesprite, while also
buying assets, to understand the process. 

Super mario world style overworld...


