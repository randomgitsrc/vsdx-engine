---
name: vsdx-diagram
description: Use when the user asks for an architecture diagram, microservice topology, business flowchart, swimlane, or network topology that must be delivered as a real editable Microsoft Visio (.vsdx) file, or when an existing .vsdx must be read, modified incrementally, re-rendered, or validated without having Visio installed. Use when the user says "画一个架构图", "生成 Visio 文件", "改一下这张图", "把这个服务改名", or asks for VSDX/PNG/SVG diagram output. Covers node/edge design, layered layout, connector routing, preview-based visual iteration, and round-trip-safe edits.
license: MIT
---

# VSDX Diagram Skill

Produce and maintain **real Microsoft Visio files** through the `vsdx-tool` CLI, without
Microsoft Visio installed. The engine writes genuine OPC/VSDX packages and renders PNG/SVG
from those packages.

This skill is about **designing and iterating the diagram**. The tool does the file work.
Never hand-write VSDX XML, and never deliver an SVG or PNG with a `.vsdx` extension.

---

## 0. Prerequisites

```bash
vsdx-tool version --json     # confirm the tool runs
```

If that fails, install per `docs/installation.md` (requires .NET 8). Then confirm the
workspace is writable and that a CJK font exists when the diagram contains Chinese:

```bash
fc-list :lang=zh | head -3
```

---

## 1. Decide the diagram family first

Do not draw every system the same way. Pick the family from what the user is describing:

| User is describing | Family | Layout shape | Node vocabulary |
| --- | --- | --- | --- |
| System internals, layers, tiers | `architecture` | Horizontal bands (access / service / data) | Boxes, hexagons for gateways |
| Service-to-service calls, event flows | `microservices` | Left-to-right pipeline + message bus column | Rounded boxes, parallelogram for queues |
| A process with branches and actors | `workflow` | Top-to-bottom or swimlane | Circles for start/end, diamonds for decisions |
| Machines, zones, trust boundaries | `network` | Zones as nested regions | Hexagons for firewalls, cylinders for stores |

Ask the user only when the intent is genuinely ambiguous. Otherwise choose and state your
choice in one line.

---

## 2. Design the structure before writing JSON

Work in this order. Skipping step 2 is the most common cause of an ugly result.

1. **List the nodes.** Give each a stable kebab-case `id` (`query-service`, `postgresql`).
   Keep ids stable forever — renames happen on `text`, not `id`, when the *thing* is the same.
2. **Assign each node to a band and a column.** Draw the grid on paper first:
   `band = row semantics`, `column = left-to-right order in the flow`.
3. **Order the pipeline so neighbours are adjacent.** If A calls B calls C, put them in
   consecutive columns. A connector between columns 1 and 5 will cross everything between.
4. **Put each data store directly under the service that owns it.** Then the edge is a clean
   vertical.
5. **Only then write coordinates.** Use a fixed column table and reuse it.

### Column grid template

```python
COL  = {1: 2.60, 2: 5.50, 3: 8.40, 4: 11.30, 5: 14.20, 6: 17.10}
BAND = {"access": 9.70, "service": 6.45, "data": 3.30}
W, H = 2.20, 1.00          # node size; keep constant within a band
```

Coordinates are **centres** in inches, origin at the page's bottom-left, so larger `y` is
higher on the page.

### Node vocabulary

`shape` accepts Visio master names or these aliases:

`rounded`, `rectangle`, `process`, `circle`, `ellipse`, `square`, `diamond`, `decision`,
`data`, `hexagon`, `parallelogram`, `trapezoid`, `pentagon`, `triangle`, `text`, `container`,
`cloud`, `database`, `queue`, `firewall`, `gateway`

Use shape **semantically**: gateway → `hexagon`, queue/bus → `parallelogram`, decision →
`diamond`, actor → `circle`, store → `process` or `data`. Two or more distinct families per
diagram is expected; one family for everything looks like a checklist.

---

## 3. Write the model

Minimal but complete example:

```json
{
  "id": "order-platform",
  "name": "订单平台架构图",
  "type": "architecture",
  "pageSize": { "width": 18.0, "height": 11.0, "unit": "inches" },
  "background": "#FFFFFF",
  "style": { "font": "Microsoft YaHei", "fontSize": 11 },
  "pages": [{
    "id": "page-0",
    "name": "Architecture",
    "size": { "width": 18.0, "height": 11.0, "unit": "inches" },
    "groups": [
      { "id": "grp_svc", "name": "服务层  Service Layer", "kind": "container",
        "autoSize": true,
        "style": { "fill": "#EDF7ED", "stroke": "#A9D08E", "textColor": "#375623",
                   "bold": true, "hAlign": "left" } }
    ],
    "nodes": [
      { "id": "api", "text": "API 网关\nKong", "shape": "hexagon",
        "bounds": { "x": 5.50, "y": 9.70, "w": 2.40, "h": 1.00 },
        "groupId": "grp_svc",
        "style": { "fill": "#F2B705", "stroke": "#BF8F00", "textColor": "#3F2E00",
                   "fontSize": 11, "bold": true },
        "shapeData": { "owner": "platform-team", "sla": "99.95%" } },
      { "id": "orders", "text": "订单服务\nOrder Service", "shape": "rounded",
        "bounds": { "x": 5.50, "y": 6.45, "w": 2.20, "h": 1.00 },
        "groupId": "grp_svc",
        "style": { "fill": "#70AD47", "textColor": "#FFFFFF", "bold": true } },
      { "id": "db", "text": "PostgreSQL", "shape": "process",
        "bounds": { "x": 5.50, "y": 3.30, "w": 2.20, "h": 1.10 },
        "style": { "fill": "#C55A11", "textColor": "#FFFFFF" } }
    ],
    "edges": [
      { "id": "e-api-orders", "from": "api", "to": "orders", "kind": "straight",
        "label": "REST", "endArrow": "triangle", "lineColor": "#507E32" },
      { "id": "e-orders-db", "from": "orders", "to": "db", "kind": "straight",
        "label": "SQL", "endArrow": "triangle", "lineColor": "#843C0C" }
    ]
  }]
}
```

Key field rules:

- `bounds` = centre + size (`x`, `y`, `w`, `h`).
- `groupId` alone is enough — the engine fills `group.members` and the container bounds.
- `shapeData` becomes real Visio Shape Data (visible in the Shape Data window).
- `edge.kind`: `straight` (default), `rightAngle`, `curved`, `dynamic`.
- `fromPort`/`toPort`: `left|right|top|bottom|auto`. Omit and let the router decide.
- `waypoints`: explicit `[{x,y},…]` when you need a route the router cannot find. Explicit
  waypoints are never overwritten.
- Colours accept `#RRGGBB`, `rgb(...)`, or CSS names. Text colour is auto-contrasted when
  omitted.

Validate against the schema if a validator is available: `schemas/diagram.schema.json`.

---

## 4. Create and render

```bash
vsdx-tool create --input model.json --output diagram.vsdx
vsdx-tool render --input diagram.vsdx --format png --output diagram.png --dpi 120
vsdx-tool render --input diagram.vsdx --format svg --output diagram.svg
```

### Or let the engine place the nodes entirely

If you are generating a diagram from a model rather than editing an existing one, you usually
do not need to hand-place anything:

```bash
vsdx-tool create --input model.json --output diagram.vsdx --layout auto --fit-page
```

`--layout` picks the arrangement; `auto` chooses from the diagram's `type`:

| Strategy | Use for | What it does |
| --- | --- | --- |
| `layered` | architecture, microservices, network, deployment | Bands top-to-bottom, nodes ordered left-to-right inside each band |
| `flow` | business processes, pipelines | Follows the arrows; with containers, one band per container |
| `tree` | org charts | Root on top, widens downward |
| `grid` | inventories, no grouping | Uniform rows and columns |
| `manual` | you pinned every coordinate | Nothing moves |

`auto` resolves to: architecture/network/microservices → `layered`; workflow **with a decision
node** → `flow`; workflow without decisions → `layered`; org → `tree`.

When containers are present, `grid` and `tree` fall back to a banded layout rather than
scattering members — grouping always wins over the nominal strategy.

Prefer authoring the model with only ids, text, `groupId` and edges, then let `--layout auto
--fit-text --fit-page` derive all geometry. Hand-writing coordinates is what makes a diagram
look assembled rather than designed.

### Let the engine size the boxes, then look

Hand-writing every `bounds` is what makes a diagram look assembled rather than designed. If
you do not have a strong reason to pin coordinates, let the engine derive size from text and
fit the page:

```bash
vsdx-tool create --input model.json --output diagram.vsdx \
  --fit-text    # node/container size from label length (+ padding)
  --fit-page    # page shrinks or grows to the drawing
  --recenter    # equal margins around the content
```

It only ever grows a box to fit its text — it never clips a label and never shrinks a box you
deliberately made large. Font size has a hard legibility floor; if content is too dense the
engine says "split the diagram" instead of quietly shrinking type.

`render` re-opens the saved package and projects its real geometry, so the preview always
matches the deliverable. CJK fonts are resolved automatically; pass `--font-file` to force a
specific face.

---

## 5. Check layout, then look at the preview

Machine checks are necessary but not sufficient. Do both.

```bash
vsdx-tool check-layout --input diagram.vsdx --json
```

Read `hardConstraintsPassed` first, then `score`.

**`hardConstraintsPassed: false` means the diagram is not deliverable.** When it is false the
score is forced to `0`, so a high score can never hide a broken layout. Fix every `error`
before looking at anything else.

| Code | Severity | Meaning | Fix |
| --- | --- | --- | --- |
| `node.overlap` | **hard** | Two nodes intersect | Re-space the column or row |
| `edge.through-node` | **hard** | A connector crosses an unrelated node | Move the node, or add `waypoints` |
| `group.overlap` | **hard** | Two container bands overlap | Separate the bands; do not just nudge one node |
| `group.member-escapes` | **hard** | A node pokes out of its container | Usually fixed by `autoSize: true` |
| `edge.detour` | soft | Connector wanders > 2× the straight distance | Move endpoints closer, or rewrite `waypoints` |
| `row.misaligned` | soft | Nodes on one row differ in Y | Give the row a single baseline |
| `row.size-jitter` | soft | Neighbouring same-family nodes differ a lot in size | Unify the row's node size |
| `group.gutter` | soft | Two stacked bands are too close | Widen the vertical step |
| `spacing.too-tight` | soft | Neighbours closer than ~0.18" | Widen the column step |
| `text.overflow-*` | soft | Label does not fit its box | Shorten text, raise `w`/`h`, or lower `fontSize` |
| `edge.crossing` | soft | Two connectors cross | Usually acceptable; reduce when easy |
| `label.overlaps-node` | soft | A label sits on a node | Move the edge or set `labelPosition` |

Every issue also carries `rootCause`, `recommendedFix` and `level`
(`Local` / `RegionRelayout` / `FullRedraw`). **Follow `level`** — patching one node when the
diagnosis says `RegionRelayout` is how a diagram gets worse with each attempt.

`edgeDetourRatio` in the JSON gives the measured detour factor per edge, so you can see which
connector is the outlier rather than guessing.

Then **open the PNG and look at it**. Ask yourself:

1. Can I follow each arrow from source to target without tracing a maze?
2. Does every layer read as one visual band?
3. Is any label unreadable, clipped, or sitting on a line?
4. Do the shapes' semantics match their roles?
5. Is the page mostly filled, or is there a large empty region?

Iterate until it looks right. "The file was produced" is not acceptance.

---

## 6. Modify an existing diagram incrementally

**Never regenerate the whole diagram to apply an edit.** Load the model, change only what
was asked, apply it onto the original package.

```bash
# 1. Read the current package into the editable model
vsdx-tool export-model --input diagram.vsdx --output current.json

# 2. Edit current.json (rename / add / move / regroup …)

# 3. Apply onto the ORIGINAL package so untouched objects keep their identity
vsdx-tool update --input diagram.vsdx --model current.json --output diagram_v2.vsdx
vsdx-tool render --input diagram_v2.vsdx --format png --output diagram_v2.png --dpi 120

# 4. Prove nothing else changed
vsdx-tool diff --before diagram.vsdx --after diagram_v2.vsdx
```

### Renaming a node

Keep the *same thing*, change only how it is labelled:

```jsonc
// Before
{ "id": "query-service", "text": "查询服务\nQuery Service", "visioShapeId": "query-service" }
// After — id changes, visioShapeId pins the existing shape so this is a rename, not a rebuild
{ "id": "analytics-service", "text": "分析服务\nAnalytics Service",
  "visioShapeId": "query-service" }
```

Every edge that pointed at `query-service` must be re-pointed to `analytics-service`.

If you omit `visioShapeId`, the engine still matches by stored stable id or by text, but
setting it explicitly is the reliable path.

### Adding a node

Append it, give it a `groupId`, and add the edges. Then check the layout again — a new node
often lands on an existing connector.

### Deleting

Remove it from `nodes` (and its edges), then pass `--delete-missing`. Without that flag the
engine keeps package content the model does not mention and says so in its report.

---

## 7. Confirm before delivering

```bash
vsdx-tool validate --input diagram.vsdx --json     # structure + OPC + Visio semantics
vsdx-tool inspect  --input diagram.vsdx --json     # counts, groups, layers, shape data
```

Deliver:

| File | Purpose |
| --- | --- |
| `diagram.vsdx` | The deliverable — real, editable Visio |
| `diagram.png` | Preview for humans and for your own iteration |
| `diagram.svg` | Vector preview / embedding |
| `diagram.json` | The model, so the next edit is cheap |

Report `hardConstraintsPassed`, the layout score, and any compatibility warnings alongside
the files. If `compatibility.issues` is non-empty, quote the codes — they are meaningful,
not noise.

---

## 8. Failure handling

| Symptom | Cause | Action |
| --- | --- | --- |
| Chinese renders as `□` | No CJK font reachable | `fc-list :lang=zh`; pass `--font-file` |
| `update` reports `edge-from-missing` | Edge references an id that is not in `nodes` | Fix the id |
| Text clipped in PNG but correct in SVG | Label did not fit its box | Enlarge the node or shorten text |
| Container border cuts through nodes | Explicit bounds smaller than members | Remove `bounds`, keep `autoSize: true` |
| `validate` fails on a foreign file | Genuinely different VSDX dialect | Read `inspect --json` warnings; report limits honestly |

---

## 9. Hard rules

1. The `.vsdx` is the deliverable. A renamed PNG/SVG is a failure.
2. Design the grid before writing coordinates.
3. Change only what the user asked for; prove it with `diff`.
4. Keep node ids stable across edits that do not rename the concept.
5. `check-layout` reporting `hardConstraintsPassed: false` blocks delivery. A soft score is
   advisory and never overrides a hard failure.
6. Open the PNG and judge it visually before claiming success.
7. Do not claim Visio-native validation — that requires a real Visio install.
8. When the engine reports a compatibility issue, surface it. Do not swallow it.
