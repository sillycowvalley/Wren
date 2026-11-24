# How we make YouTube videos

A guide for a project starting a channel of its own (the Wren Project
first), taken from how the Reggie-16 channel's videos are made. It
stands alone: nothing in it needs the TTLSim repository. Where it
gives an example, the example is a Reggie-16 video, so read "the
machine" as whatever your project builds.

Michael records and publishes. Claude writes the script, builds the
slides, the backdrops and the thumbnail, and harvests every console
line and figure the video shows. Everything Claude makes is a file in
the repository, beside the documents it draws on.

---

## 1. What a video is made of

Every video is two things:

1. **A script**, one Markdown file, `Series_NN_Name.md`, in a
   `Videos` folder beside the project's documents.
2. **A set of images**: slides, cut-away backdrops, a closing
   backdrop and a thumbnail, in a folder of their own,
   `Videos/Infographics/<Series_NN>`, each PNG beside the one HTML file
   it is rendered from.

Nothing else is made by hand. Michael films only what the script's shot
list names.

---

## 2. Choosing what to make

**A name the viewer already knows draws about three times the clicks
of an explanation.** On the Reggie channel, from release to November
2026:

| Thumbnail's headline | Views a day | Watched a view |
|---|---:|---|
| FORTH | 82 | 22% |
| GALFORGE | 74 | 28% |
| IT RUNS ZORK. | 66 | 29% |
| TTL CPU PROJECT (the trailer) | 60 | 31% |
| SPYING ON A TTL CPU | 35 | 19% |
| HOW A TTL CPU RUNS UNIX | 26 | 24% |
| TTL CPU RUNS UNIX | 25 | 22% |
| 16-BIT ARM THUMB? | 22 | 18% |

What that teaches:

- **The headline is a name**: a game, a language, a tool, a famous
  machine. The explanation goes in the title and the video, never in
  the thumbnail's big words.
- **Deep dives hold viewers longest and draw the fewest clicks.** Their
  trouble is the package, not the content. Package one as a claim or a
  name, never as "how".
- **A community that will pass it on carries a niche subject.** The
  Forth video had a Forth group; GalForge had the GAL and CUPL users.
  Every script names where the video is to be posted.
- **Retention is 18 to 31% everywhere.** A short video keeps a larger
  share, a long one more minutes. Nothing says to cut a deep dive short.

The figures are YouTube Studio's (Analytics, Content). Keep the
project's own table in its `Videos/README.md` and let it overrule this
one once it has a few videos.

---

## 3. Planning the length

**Budget about 40 spoken words a minute, everything included.** A
script of 1,227 spoken words over eleven slides and four cut-aways
recorded at 30 minutes: delivery, pauses, cut-aways and camera moves
together run at about a third of a reading pace.

| Video length | Spoken words |
|---|---|
| 6 minutes | about 250 |
| 10 minutes | about 400 |
| 15 minutes | about 600 |
| 30 minutes | about 1,200 |

A slide with its narration takes about two and a half minutes. A
script's part timings are written from this budget; the published
chapters are taken from the edit.

---

## 4. The script

A script has these sections, in this order.

### The head

A title line, a paragraph saying what the video is and who it is for,
then:

- **Length**: the minutes and the spoken word count.
- **The viewer leaves knowing**: one sentence, the video's promise.
- Where the slides are, and where every figure comes from (the
  documents, the benchmark, the runs).

### On YouTube

Everything Michael pastes into YouTube, ready to paste:

- **Title.** On desktop the whole title shows on two lines of about 50
  characters; in the sidebar and on phones about 40. Put the name in
  the first 40. Give two alternatives under it.
- **Description**, in a code block: what the thing is, what's in the
  video, a link where a viewer can try it, then the hashtags.
- **Chapters**, in a code block, from the script's timings, with the
  note "take the times from the edit before posting".
- **Where to post it**: the groups and forums that will pass it on.

### Before recording

A numbered list of everything that must be true before the camera
rolls: the build the machine or web page must carry, the machine's
state (booted to its prompt, which disk in it), the slides open and how
to step them, the camera's view, the sound, where to park the mouse
pointer, and the thumbnail's file name.

### The script

One numbered part a slide or a scene, each headed with its timing and
its file: `### 3. The listing (1:40–3:00) — Forth_02_Listing.png`.
Under the heading, in this order:

- a line in plain text of what to do (open the slide, switch to the
  cut-away scene, type this, cut the wait in the edit);
- every console line to type or see, in a code block;
- the narration.

**The narration is one line a paragraph, beneath a `> `**, so it
pastes whole into a prompter.

**The narration never reads the slide.** The slide carries the list
(names, figures, what each part does); the narration says what the
slide doesn't: why it matters, the story behind it, what to look at. No
sentence of the narration repeats one on its slide, and a figure on the
slide is said only where the point rests on it.

**Every console line is from a run** of the exact keystrokes on the
image the video is shot on, never written from memory. A figure only
the real machine can give (a wall-clock time) is the bench's, and the
script says so. Timings are at the clock the video is shot at, and the
script names it.

### Shot list

A table of every live scene: when it plays, which backdrop, what the
console and the camera show. Then "Nothing else is filmed." A video of
slides alone says "The slides, live in Chrome, in order. Nothing is
filmed."

---

## 5. Slides

Plain HTML and CSS, no script, in the house card style: slides at
1920 × 1080 with a device pixel ratio of 2 (rendered 3840 × 2160).

### The house style

- **Navy and its grid**: background `#0f1b2d` with a faint 40 px grid
  and a radial highlight at the top right; the headline in gold
  (`#fde68a`) with its second half in white.
- **One colour to a meaning across every slide**, set once for the
  channel and never reused for anything else. Reggie's: green for UNIX
  and user mode, gold for the kernel and the point being made, red for
  faults, blue for memory and the serial port.
- **A chip at the top left** names the series, the video and the
  slide's place: `REGGIE-16 · THE 4 MHZ BOARD · 1/8`.
- **One idea to a slide**, sized to be read while the narration runs: a
  headline, a picture of the idea, and words only where the picture
  can't carry them.
- **Diagrams and graphs over text**: a state diagram for modes, a
  funnel for every way into something, a table of ticks and crosses for
  a comparison, a bar for a share.
- **Fonts**: Liberation Sans (Arial has its metrics) and DejaVu Sans
  Mono, so a render on Windows lands where it did here.

### The opening comment

Every HTML file opens with a comment that says what it is, which PNG
it is the source of, how to render it, and **where each of its facts
comes from**: the document a figure is from, the run a listing was
harvested from, the document a chip's name was read from.

### Patterns that work

- **Walking a block diagram.** One block diagram, drawn once in SVG,
  shown again on each slide with only the part being talked about lit:
  the rest at a fifth of its opacity, the lit boxes and wires gold, a
  caption bar beneath saying what the lit part does. Stepping through
  plays like an animation, and the viewer always sees where the part
  sits.
- **The parts on the board.** A photo of the real board, darkened
  everywhere but the chips that do the job: each boxed in gold with a
  numbered marker, and a legend beside the photo naming it. The boxes
  are drawn in the photo's own pixels, and an SVG mask cuts them out of
  the darkening. **Every chip is named from the board's documents**,
  never guessed from the photo, and **by its part (a 74HC74), never its
  designator (U22)**. A part the photo doesn't show clearly is named in
  the legend and left unboxed.
- **Graphs from runs.** A graph shows what a run produced, and a
  console panel on a slide is a run's console.

### Animated slides (the default for walkthroughs)

A slide draws itself while Michael talks: the photo dims, gold boxes
draw themselves round the slide's region one after another, the
numbered markers pop in and the text rises, in about three seconds. It
is all CSS (`@keyframes`; a box draws by its `stroke-dashoffset` falling
to 0, each box with its own `animation-delay`), so it needs no script
and no video file.

It is shown live: open the first slide in Chrome and press F11. The
animation plays as the page opens. Two links, each covering half the
page, make it a slideshow: a click on the right half opens the next
slide, on the left half the one before; F5 plays a slide again. The
thumbnail is slide 0, so the video can open on it and click into
slide 1.

The PNG beside each one is its last frame, rendered in headless
Chromium after the animations finish, for the image viewer and for
checking.

### Photos for a walkthrough

Michael supplies one photo of each board: square on, under even light,
on the mat, the board filling most of the frame, the same way up every
time, at the camera's full resolution (a photo sent from the phone app
arrives full size; the desktop app shrinks it). The board as it runs:
its repairs fitted, its labels on. The boxes are placed in the photo's
pixels, so a new photo means placing them again.

---

## 6. Cut-aways, and the closing backdrop

**A cut-away to the live machine gets a backdrop of its own**: the
slide's chip and title across the top, a black console frame for the
terminal, and where something physical matters (lamps, a screen, a
speaker) a 16:9 camera frame. Otherwise a "what you'll see" card
explaining the console's lines. Its chip carries the progress number of
the slide it cuts away from.

**Every backdrop keeps the same frames**, so one OBS scene serves all
of them with only the image swapped:

| Frame | Position in the 1920 × 1080 canvas | Size |
|---|---|---|
| console | 48, 152 | up to 1114 × 762 |
| camera | 1210, 152 | 662 × 372, 16:9 |

The frames are black inside, so a source scaled to fit leaves no
border. In an animated set a backdrop is a page in the sequence and
stands still, so its frames are in place the moment it appears.

**The closing backdrop** puts the video's points again, ticked, with
"Thanks for watching", all in the left 60%: the right 40% is left
empty for YouTube's end-screen elements.

---

## 7. The thumbnail

1280 × 720 with a device pixel ratio of 1.5 (rendered 1920 × 1080,
under YouTube's 2 MB). One family for the whole channel:

- the chip;
- a short line over the headline;
- **a headline in huge type that leads with a name the viewer already
  knows** (ZORK, FORTH, GALFORGE);
- on the right, a picture that is the video's own and real: a terminal,
  the game's screen, the board's photo;
- the channel's mark (Reggie's is "TTL CPU", in the smaller line, never
  the headline) and a recurring motif (Reggie's row of lamps).

Each headline is the video's alone, so two thumbnails never read alike
side by side. The headline must read at the size YouTube lists videos;
the smaller lines are for the watch page, so nothing the headline needs
goes in them. A Facebook image for the post that links the video is
1200 × 630, in the same family.

**Rendering, on Windows**: open the HTML in Edge or Chrome, press F12,
then Ctrl+Shift+M for the device toolbar. Set the dimensions to
Responsive, 1280 × 720; from the toolbar's menu (the three dots)
choose "Add device pixel ratio" and set it to 1.5; then "Capture
screenshot" from the same menu, and save over the PNG. A slide is the
same at 1920 × 1080 and a ratio of 2.

---

## 8. The folder

**The folder sorts in the order the video plays**, so the Windows
image viewer steps through it with the arrow keys from the thumbnail to
the closing slide.

| File | What it is |
|---|---|
| `Series_NN_00_Thumbnail` | the thumbnail (and slide 0 of an animated set) |
| `Series_NN_01_Name` … | the slides, in order |
| `Series_NN_05b_Name` | shown after slide 5: a cut-away's backdrop, a photo |
| `Series_NN_11c_Name` | slide 11 again, after `11b`: a copy, so the order holds |
| `Series_NN_13_Closing` | the closing backdrop |
| `Series_NN_Photo_Board.jpg` | a photo a slide draws on, sorting after the slides |

A slide that comes back later is a copy at each place it plays, its
HTML naming the one it follows.

---

## 9. Recording

- **Slides**: live in Chrome at F11 (animated), or the PNGs in the
  Windows image viewer, stepped with the arrow key.
- **Live scenes**: OBS, one scene per backdrop layout, the terminal
  captured into the console frame and the camera into the camera frame.
- **The pointer**: parked at the edge of the screen after each click,
  or hidden in the capture.
- **Let each animated slide finish** its three seconds before speaking
  over the boxes it is drawing.
- **Sound that is the point** (a sound card, a tune) is recorded with
  the video, from a microphone in front of it.
- **Waits** (a boot, a long build) are cut in the edit, and the script
  says where.
- **The narration** is read from a prompter, one paragraph a line.

After the edit, take the chapter times from it, paste the On YouTube
section, set the thumbnail, add the end screen over the closing
backdrop's right 40%, and post where the script says.

---

## 10. Claude's checklist for a new video

1. Pick the name the video leads with (section 2) and the community
   that will pass it on.
2. Set the length and the word budget (section 3).
3. Harvest every console line and figure from real runs; record where
   each came from.
4. Write the script in the order of section 4, the narration never
   reading the slide.
5. Build the slides, backdrops, closing backdrop and thumbnail in the
   house style, each HTML's opening comment naming its sources and its
   render steps; render every PNG.
6. Check the folder steps through in playing order.
7. Hand Michael the script's Before recording and Shot list: they are
   the whole of what he does at the bench.
