#!/usr/bin/env python3
"""Derives the round-2 model from the round-1 export and applies the five required edits.

Run order:
    vsdx-tool export-model --input demo/architecture.vsdx --output demo/architecture.json
    python3 tools/make_v2.py
    vsdx-tool update --input demo/architecture.vsdx --model demo/architecture_v2.json \
                     --output demo/architecture_v2.vsdx --delete-missing

Starting from the *exported* model (not a hand-written copy) is what makes this a genuine
incremental edit: every id and coordinate in the input comes from the package on disk.
"""
import json

SRC = "demo/architecture.json"
DST = "demo/architecture_v2.json"

with open(SRC, encoding="utf-8") as f:
    model = json.load(f)

page = model["pages"][0]
nodes = {n["id"]: n for n in page["nodes"]}

# ---------------------------------------------------------------------------
# 1. Query Service -> Analytics Service
#    The Visio shape is reused in place: the agent-facing id changes, the
#    visioShapeId pins the existing shape so this is a rename, not a replace.
# ---------------------------------------------------------------------------
query = nodes["query-service"]
query["id"] = "analytics-service"
query["name"] = "Analytics Service"
query["text"] = "查询/分析服务\nAnalytics Service"
query["visioShapeId"] = "query-service"
query["style"]["fill"] = "#2F7D8C"
query["style"]["stroke"] = "#1F5A66"
query["shapeData"]["owner"] = "analytics-team"
query["shapeData"]["sla"] = "99.9%"
nodes["analytics-service"] = query

# ---------------------------------------------------------------------------
# 2. Add Audit Service to the service band
# ---------------------------------------------------------------------------
audit = {
    "id": "audit-service",
    "name": "Audit Service",
    "type": "service",
    "text": "审计服务\nAudit Service",
    "shape": "rounded",
    "bounds": {"x": 14.20, "y": 6.45, "w": 2.20, "h": 1.00},
    "groupId": "grp_services",
    "layers": ["app"],
    "style": {"fill": "#8E6BB5", "stroke": "#5B4180", "textColor": "#FFFFFF",
              "fontSize": 11, "bold": True},
    "shapeData": {"owner": "security-team", "tier": "service", "language": "Rust"},
}
page["nodes"].append(audit)
nodes["audit-service"] = audit

# ---------------------------------------------------------------------------
# 3. Lay the diagram out on a strict column grid.
#    Putting audit-service after report-engine keeps the pipeline strictly
#    left-to-right: ingest -> analytics -> report -> audit -> scheduler -> bus.
# ---------------------------------------------------------------------------
COL = {1: 2.60, 2: 5.50, 3: 8.40, 4: 11.30, 5: 14.20, 6: 17.10, 7: 20.00}
SERV, DATA, ACC = 6.45, 3.30, 9.70

place = {
    # access band
    "web-portal": (1, ACC), "api-gateway": (2, ACC),
    "auth-service": (4, ACC), "cdn-edge": (6, ACC),
    # service band
    "ingest-service": (2, SERV), "analytics-service": (3, SERV),
    "report-engine": (4, SERV), "audit-service": (5, SERV),
    "scheduler": (6, SERV), "event-bus": (7, SERV),
    # data band, each store under the service that owns it
    "postgresql": (2, DATA), "redis-cache": (3, DATA), "data-lake": (4, DATA),
    "audit-log": (5, DATA), "prometheus": (7, DATA),
}
for node_id, (col, y) in place.items():
    if node_id in nodes:
        nodes[node_id]["bounds"] = {"x": COL[col], "y": y,
                                    "w": nodes[node_id]["bounds"]["w"],
                                    "h": nodes[node_id]["bounds"]["h"]}

# mobile-app sits in the left gutter, below web-portal but off the gateway column.
nodes["mobile-app"]["bounds"]["x"] = 2.60
nodes["mobile-app"]["bounds"]["y"] = 8.10

page["size"] = {"width": 22.4, "height": 12.0, "unit": "inches"}
model["pageSize"] = page["size"]

# ---------------------------------------------------------------------------
# 4. Rebuild the edge set for the new pipeline. Every edge connects shapes that
#    are neighbours in the flow, so routing stays short and legible.
# ---------------------------------------------------------------------------
def edge(eid, frm, to, label, color, weight=0.014, pattern=None, kind="straight"):
    e = {"id": eid, "from": frm, "to": to, "kind": kind, "label": label,
         "lineColor": color, "lineWeight": weight, "endArrow": "triangle"}
    if pattern:
        e["linePattern"] = pattern
    return e


page["edges"] = [
    # access band
    edge("e-web-gw", "web-portal", "api-gateway", "HTTPS", "#2E75B6"),
    edge("e-mobile-gw", "mobile-app", "api-gateway", "gRPC", "#2E75B6"),
    edge("e-gw-auth", "api-gateway", "auth-service", "verify token", "#BF8F00", 0.012),
    edge("e-auth-cdn", "auth-service", "cdn-edge", "session", "#999999", 0.010, 2),
    # access -> service
    edge("e-gw-ingest", "api-gateway", "ingest-service", "write API", "#507E32"),
    # service pipeline (audit sits between report and scheduler)
    edge("e-ingest-analytics", "ingest-service", "analytics-service", "dataset", "#507E32", 0.012),
    edge("e-analytics-report", "analytics-service", "report-engine", "aggregate", "#507E32", 0.012),
    edge("e-report-audit", "report-engine", "audit-service", "audit summary", "#8E6BB5", 0.012),
    edge("e-audit-sched", "audit-service", "scheduler", "compliance", "#8E6BB5", 0.010),
    edge("e-sched-bus", "scheduler", "event-bus", "publish", "#BF8F00", 0.012),
    # service -> data
    edge("e-ingest-pg", "ingest-service", "postgresql", "SQL", "#843C0C"),
    edge("e-analytics-redis", "analytics-service", "redis-cache", "cache", "#7F0000", 0.010),
    edge("e-report-lake", "report-engine", "data-lake", "Parquet", "#843C0C", 0.012),
    # 3. Audit Service -> PostgreSQL (explicitly required by the task) and its own store.
    # PostgreSQL sits under the ingest column, so the route drops from audit-service, runs
    # left along the band gap below the service row, then enters the store from the top-left.
    {**edge("e-audit-pg", "audit-service", "postgresql", "audit rows", "#843C0C", 0.012),
     "kind": "rightAngle", "fromPort": "bottom", "toPort": "top",
     "waypoints": [{"x": 14.20, "y": 4.55}, {"x": 5.50, "y": 4.55}]},
    edge("e-audit-store", "audit-service", "audit-log", "append-only", "#5B4180", 0.010),
    edge("e-bus-prom", "event-bus", "prometheus", "metrics", "#7030A0", 0.010, 2),
]

# ---------------------------------------------------------------------------
# 5. Rename a group
# ---------------------------------------------------------------------------
for group in page["groups"]:
    if group["id"] == "grp_data":
        group["name"] = "数据层  Data Platform Layer"
    group["autoSize"] = True

# keep membership in sync with each node's groupId
for group in page["groups"]:
    group["members"] = [n["id"] for n in page["nodes"] if n.get("groupId") == group["id"]]

with open(DST, "w", encoding="utf-8") as f:
    json.dump(model, f, ensure_ascii=False, indent=2)

print(f"wrote {DST}: {len(page['nodes'])} nodes, {len(page['edges'])} edges, "
      f"{len(page['groups'])} groups")
