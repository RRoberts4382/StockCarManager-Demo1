# Stock Car Manager task list

- [x] Revisit Startup Issues

  **Symptom and log evidence**

  - On the affected Windows PC (GeForce RTX 3050, driver 32.0.16.1088), launching the game directly from the Start menu left it running in Task Manager with no visible window.
  - The 0.1.16 Direct3D 12 log and 0.1.17 Direct3D 11 log both reached Mono assembly reload and PhysX initialization, then ended at "<RI> Initializing input." and "Using Windows.Gaming.Input". The failing 0.1.18 Start menu launch ended at the same point on Direct3D 11. A successful local launch continued to "UnloadTime" and opened a responsive window. The last line indicates where logging stopped, but the cause is still unknown. The Direct3D 12 info-queue warning (0x80004002) also appeared on a successful local launch.

  **What we tried**

  - 0.1.16 opened from PowerShell with Direct3D 11 and a 1024 × 768 window. The game then switched to fullscreen when it applied its display setting.
  - 0.1.17 made Direct3D 11 the Windows default. Start menu launch still stalled, but launching from PowerShell with the windowed arguments opened a responsive game.
  - 0.1.18 made Unity start in a 1024 × 768 window before applying the saved display mode. It kept Direct3D 11 and added the same windowed arguments to the Start menu shortcut. The direct shortcut still stalled on the affected PC even though its target, quoting, and arguments were checked. The same installed executable and arguments worked from PowerShell when launched from the game directory.

  **Working workaround**

  Open Windows PowerShell and run this single line for the default install location:

  ~~~powershell
  Set-Location (Join-Path $env:LOCALAPPDATA 'Programs\Stock Car Manager'); & '.\Stock Car Manager.exe' -screen-fullscreen 0 -screen-width 1024 -screen-height 768
  ~~~

  The latest 0.1.18 installer gives both the Start menu and desktop shortcuts a hidden Windows PowerShell launcher, the game directory as the working directory, and the same windowed arguments. The affected user confirmed the updated Start menu shortcut works. Desktop shortcut behavior on that PC has yet to be confirmed.

  **When revisiting**

  - The final 0.1.19 test installer restores automatic graphics API selection and fullscreen startup. Both shortcuts launch the executable directly. It retains the menu axis change described below. A local direct launch selected Direct3D 12 and reached a responsive window. Whether the axis change affects the earlier startup stall remains unknown.
  - On the affected PC, install the latest rebuilt 0.1.19 over the existing installation. Leave the wheel and pedals connected, close any running game process, and launch from Start. Confirm that the startup menu appears and responds. Close the game and repeat from the desktop shortcut.
  - If either launch stalls, copy its Player.log before launching again and record the shortcut and last line reached. Repeat the failed launch with the wheel and pedals disconnected to isolate the hardware difference. The PowerShell command above remains available for comparison.

- [x] Determine why buttons are flickering on the startup screen

  - The affected PC showed rapid button highlighting on the startup screen, in Track View, and elsewhere while the wheel and pedals were connected. The project used the same highlight for focus and hover on several controls, so repeated navigation could look like mouse movement. The connected devices are a suspected source of input.
  - 0.1.19 removes joystick and gamepad stick axes from the shared UI Navigate action. Keyboard navigation, mouse input, the gamepad D-pad, and driving bindings are retained. The built player was checked for the updated bindings. The connected-hardware test is pending.
  - With the wheel and pedals connected and untouched, leave the mouse still on the startup screen, another menu, and Track View. Check that button highlights stay steady and that mouse clicks and keyboard navigation still work. Check any driving controls he normally uses.
  - If flickering persists, note the affected screens and whether the pointer itself moves or only the button highlight changes. Close and relaunch with both devices disconnected. If that stops it, reconnect one device at a time and relaunch between tests to identify the trigger.
- [x] Import manufacturers with logos into a data-driven catalog and give each manufacturer a stable unique ID. Replace enum-based manufacturer storage with catalog IDs and let users add and delete manufacturers. Keep existing saves compatible by mapping legacy enum values to the corresponding IDs on load.
- [x] Import series, with logo
- [x] Implement LOD auto-reduction based on frame rate limits
- [x] Import tracks, with logos
- [x] Add leader blocking at restrictor-plate tracks
- [x] Rival Crew Chief Strategy
- [x] Rubber buildup and skid marks
- [x] Cleanup settings and Configuration
- [ ] Add visual suspension movement and underbody sparks in Track View.

  - Use small, track-driven dips and bumps to move the body along the car's local up axis. Let it compress, rebound, and settle naturally. Keep wheel and tire height tied to the pavement and retain their steering and spin.
  - Use the existing separate wheel meshes and pivots. Verify all five track car LODs and carry the current body offset across detail changes so the body does not jump.
  - Emit a brief burst of backward-trailing sparks from the lowest affected point under the car when the compressed body reaches its visual scrape clearance. Peak compression alone may still leave clearance.
  - Let low tire pressure slightly reduce visual clearance at the affected corner, making a scrape a little more likely and the burst a little stronger. Require an actual visual scrape to emit sparks.
  - Show underbody sparks only at night.
  - Keep the first pass visual and preserve handling and collision. Start with a few subtle bumps on one track and review the body motion, wheel contact, and sparks from the chase camera before broader rollout.

  **Track bump reference notes**

  Texas, Las Vegas, and Pocono are the priority examples from the Cup schedule notes. Reported causes include uneven settling over access tunnels, seasonal soil movement, and aging asphalt that develops washboard ridges. The Next Gen car's stiff suspension and flat underbody make it prone to bottoming out over sharp bumps, which can upset the car or trigger a spin. [Texas reporting](https://frontstretch.com/2026/04/30/curious-case-texas-motor-speedway/) · [Pocono reporting](https://speedwaydigest.com/index.php/news/nascar-cup-series-news/24748-bumps-in-tunnel-turn-causing-problems-for-teams-at-pocono/) · [Las Vegas engineer interview](https://www.theautopian.com/a-nascar-engineer-explains-why-bumpy-las-vegas-motor-speedway-is-such-a-challenging-track/) · [Discussion of bump formation](https://www.reddit.com/r/NASCAR/comments/1kh7t3f/how_do_bumps_form_on_racetracks/)

  | Track | Primary problem area | Reference notes |
  | --- | --- | --- |
  | Texas Motor Speedway | Turns 3 and 4 | Settling above an infield access tunnel created a severe bump. The outside car can bottom out and spin when running side by side through the corner. |
  | Las Vegas Motor Speedway | Turns 1 and 2 | Aging asphalt has washboard ridges across the preferred line, plus a major bump over the infield tunnel. Teams balance shock response here against the smoother Turns 3 and 4. |
  | Pocono Raceway | Turn 2 (Tunnel Turn) | Winter pavement heave and the tunnel beneath Turn 2 contribute to bumps despite patching. The high-speed, right-angle corner can lift the front splitter. |
  | Circuit of The Americas (COTA) | Heavy braking zones and the esses | Shifting clay beneath the 3.41-mile road course contributes to deep undulations that unsettle cars under braking and rapid direction changes. |
  | Dover Motor Speedway | Corner entries and exits | Abrupt transitions onto and off the steep concrete banking can sharply compress the suspension. |

  **Historical examples**

  - **Daytona International Speedway (pre-2010 repave).** The old asphalt was rough enough that teams used softer suspension setups to keep the cars settled. A pothole opened during the 2010 Daytona 500. [Daytona repaving report](https://www.news-journalonline.com/story/news/2010/07/02/daytonas-repaving/30718108007/)
  - **Atlanta Motor Speedway (pre-2022 reconfiguration).** The worn 1997 pavement was known for rough, tire-wearing bumps that rewarded car control. [Historical discussion](https://www.reddit.com/r/NASCAR/comments/1anpd1p/how_much_more_difficult_was_it_to_drive_on/) · [Video reference](https://www.youtube.com/watch?v=FAAny3J033k)

- [ ] Make loose setups visibly yaw under cornering load in Track View.

  - Reuse the existing rear-tire slip yaw effect, which rotates the car around its front axle so the rear steps outward. Feed the existing loose/tight handling balance into that effect.
  - Give a mildly loose car a small additional rotation through the affected part of the corner. Let the car settle smoothly as cornering load eases. Preserve current yaw behavior for balanced setups.

- [ ] Add visible body-panel damage from contact in Track View.

  - Reuse existing impact events and capture the approximate contact location, direction, and severity to identify the affected panel on each car.
  - Start with pre-shaped dents for the front and rear bumpers, fenders, and doors. Show scuffs for light contact and blend in deeper dents for stronger impacts.
  - Add doughnuts, or swirling rubber marks, on the sides of cars where a spinning tire rubs against the bodywork. Place the mark at the contact point.
  - Store damage and marks by car and panel so they persist across all five track car LODs. Check whether each model uses separate panels or a combined body mesh, and preserve panel seams and wheel openings during deformation.
  - Prototype one car with a wall scrape and a car-to-car bump. Keep the first pass visual and preserve collision and handling.

- [ ] Reduce the brightness of Bristol's lights during night sessions.
- [x] Launch fireworks from the top of the backstretch grandstands at venues that have them.
- [ ] When fireworks begin while TV view is active, switch to Spotter view. Return to TV view when the fireworks finish.
- [ ] Add random camera flashes in the grandstands at the start of the race.
- [ ] Add zoom controls to the TV camera in Track View.
- [ ] Make transverse concrete slab seams much less visually pronounced on racing surfaces. Keep them narrow and low contrast so they read up close and fade smoothly at a distance.
- [ ] Use Spacebar to pause and resume the active session in Track View.
- [ ] Highlight the camera-focused driver's name in Petty Blue in the Track View standings. Move the highlight when the camera switches cars and clear it when the camera has no focused car.
- [ ] Add a retaining fence to the top of the inside track wall, styled like the outer catch fence. Keep existing wall openings clear.
- [ ] Lower the track billboards so the bottom edge of each sign is just visible above the wall from the in-car camera.
- [ ] Revise the green-flag lane transition so the field moves smoothly from formation lanes toward the preferred line as traffic gaps open. Preserve side-by-side clearance and the approved preferred line.

- [ ] Fix pit stall boxes.

- [ ] Rebuild the pit stop setup form and add a track bar adjustment.

- [ ] Add anti-aliasing to painted track lines.

- [ ] Fix the bar obstructing the Camaro in-car camera and check all car models for similar obstructions.

- [ ] Investigate and fix why too many cars miss pit-road entry during caution stops.

- [ ] Extend rubber buildup farther onto the straights.

- [ ] Freeze the race standings after all cars have finished.

- [ ] Add more spotter radio phrases.

- [ ] Add driver radio phrases for when the car is doing well.

## Convert procedural racing surfaces to imported geometry

- [ ] Convert Bowman Gray Stadium to imported racing geometry.
- [ ] Convert Bristol Motor Speedway to imported racing geometry.
- [ ] Convert Charlotte Motor Speedway to imported racing geometry.
- [ ] Convert Chicagoland Speedway to imported racing geometry.
- [ ] Convert Darlington Raceway to imported racing geometry.
- [ ] Convert Daytona International Speedway to imported racing geometry.
- [ ] Convert EchoPark Speedway to imported racing geometry.
- [ ] Convert Homestead-Miami Speedway to imported racing geometry.
- [ ] Convert Indianapolis Motor Speedway to imported racing geometry.
- [ ] Convert Nashville Fairgrounds Speedway to imported racing geometry.
- [ ] Convert Nashville Superspeedway to imported racing geometry.
- [ ] Convert New Hampshire Motor Speedway to imported racing geometry.
- [ ] Convert Orange County Speedway to imported racing geometry.
- [ ] Convert Pocono Raceway to imported racing geometry.
- [ ] Convert Riverside Oval to imported racing geometry.
- [ ] Convert Rockingham Speedway to imported racing geometry.
- [ ] Convert Sonoma Raceway to imported racing geometry.
- [ ] Convert South Boston Speedway to imported racing geometry.
- [ ] Convert Talladega Superspeedway to imported racing geometry.
- [ ] Convert Texas Motor Speedway to imported racing geometry.
- [ ] Convert Watkins Glen International to imported racing geometry.
- [ ] Convert World Wide Technology Raceway to imported racing geometry.
