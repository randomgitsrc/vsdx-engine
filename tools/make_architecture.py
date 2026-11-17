#!/usr/bin/env python3
"""Generates the round-1 demo model: a 15-node enterprise analytics platform.

Design rules encoded here (and enforced by `vsdx-tool check-layout`):

* Three horizontal bands (access / service / data), each a real Visio container.
* Six logical columns. A node belongs to a column; cross-band edges are vertical
  whenever the producer and consumer share a column, so routing stays legible.
* The service band is a left-to-right pipeline (ingest -> query -> report ->
  scheduler -> bus) so every service edge is a short horizontal run between
  neighbouring shapes.
* Every data store sits directly under the component that owns it.
"""
import json

COL = {1: 2.60, 2: 5.35, 3: 8.10, 4: 10.85, 5: 13.60, 6: 16.35, 7: 19.10}
ACCESS_Y = 9.60
SERVICES_Y = 6.45
DATA_Y = 3.30
W, H = 2.20, 1.00
DW, DH = 2.20, 1.10


def node(nid, col, y, w, h, text, shape, fill, stroke, text_color, group, layer,
         owner, tier, extra=None, bold=True, size=11):
    nd = {
        "id": nid, "name": nid, "type": tier, "text": text, "shape": shape,
        "bounds": {"x": COL[col], "y": y, "w": w, "h": h},
        "groupId": group, "layers": [layer],
        "style": {"fill": fill, "stroke": stroke, "textColor": text_color,
                  "fontSize": size, "bold": bold},
        "shapeData": {"owner": owner, "tier": tier},
    }
    if extra:
        nd["shapeData"].update(extra)
    return nd


nodes = [
    # ---- Access layer (y = 9.60) -------------------------------------------
    node("web-portal", 1, ACCESS_Y, 2.30, 0.95, "Web 门户\nReact SPA", "rounded",
         "#2E75B6", "#1F4E79", "#FFFFFF", "grp_access", "edge", "frontend-team", "edge",
         {"sla": "99.95%"}),
    node("mobile-app", 1, 8.05, 2.30, 0.95, "移动端\niOS / Android", "rounded",
         "#2E75B6", "#1F4E79", "#FFFFFF", "grp_access", "edge", "mobile-team", "edge"),
    node("api-gateway", 2, ACCESS_Y, 2.40, 1.00, "API 网关\nKong", "hexagon",
         "#F2B705", "#BF8F00", "#3F2E00", "grp_access", "edge", "platform-team", "edge",
         {"rateLimit": "10k rps"}),
    node("auth-service", 3, ACCESS_Y, W, 0.95, "认证服务\nOAuth2 / JWT", "rounded",
         "#5B9BD5", "#2E75B6", "#FFFFFF", "grp_access", "edge", "security-team", "edge"),
    node("cdn-edge", 4, ACCESS_Y, 1.15, 1.00, "CDN\n边缘节点", "circle",
         "#8FAADC", "#2E75B6", "#FFFFFF", "grp_access", "edge", "infra-team", "edge",
         bold=False, size=10),

    # ---- Service layer (y = 6.45): left-to-right pipeline -------------------
    node("ingest-service", 2, SERVICES_Y, W, H, "数据接入服务\nIngest Service", "rounded",
         "#70AD47", "#507E32", "#FFFFFF", "grp_services", "app", "data-platform", "service",
         {"language": "Go"}),
    node("query-service", 4, SERVICES_Y, W, H, "查询服务\nQuery Service", "rounded",
         "#70AD47", "#507E32", "#FFFFFF", "grp_services", "app", "analytics-team", "service",
         {"language": "C#/.NET 8"}),
    node("report-engine", 5, SERVICES_Y, W, H, "报表引擎\nReport Engine", "rounded",
         "#70AD47", "#507E32", "#FFFFFF", "grp_services", "app", "analytics-team", "service",
         {"language": "Java"}),
    node("scheduler", 6, SERVICES_Y, W, H, "任务调度\nScheduler", "process",
         "#A9D18E", "#507E32", "#243B14", "grp_services", "app", "data-platform", "service",
         bold=False),
    node("event-bus", 7, SERVICES_Y, W, H, "事件总线\nKafka", "parallelogram",
         "#FFC000", "#BF8F00", "#3F2E00", "grp_services", "app", "data-platform", "service",
         {"throughput": "500 MB/s"}),

    # ---- Data layer (y = 3.30) --------------------------------------------
    node("postgresql", 2, DATA_Y, DW, DH, "PostgreSQL\n主数据仓库", "process",
         "#C55A11", "#843C0C", "#FFFFFF", "grp_data", "data", "dba-team", "data",
         {"engine": "PostgreSQL 16", "backup": "PITR 7d"}),
    node("redis-cache", 4, DATA_Y, 1.10, DH, "Redis\n缓存", "circle",
         "#C00000", "#7F0000", "#FFFFFF", "grp_data", "data", "dba-team", "data",
         {"engine": "Redis 7", "mode": "cluster"}, bold=False, size=10),
    node("audit-log", 3, DATA_Y, DW, DH, "审计存储\nAppend-only", "data",
         "#ED7D31", "#843C0C", "#FFFFFF", "grp_data", "data", "security-team", "data",
         {"retention": "7y"}),
    node("data-lake", 5, DATA_Y, DW, DH, "数据湖\nS3 / Parquet", "parallelogram",
         "#ED7D31", "#843C0C", "#FFFFFF", "grp_data", "data", "data-platform", "data",
         {"retention": "3y"}),
    node("prometheus", 7, DATA_Y, W, DH, "监控与告警\nPrometheus", "hexagon",
         "#7030A0", "#4B1F6F", "#FFFFFF", "grp_data", "data", "sre-team", "platform",
         {"retention": "90d"}, bold=False, size=10),
]


def edge(eid, frm, to, kind, label, color, weight=0.014, pattern=None,
         from_port=None, to_port=None):
    e = {"id": eid, "from": frm, "to": to, "kind": kind, "label": label,
         "lineColor": color, "lineWeight": weight, "endArrow": "triangle"}
    if pattern:
        e["linePattern"] = pattern
    if from_port:
        e["fromPort"] = from_port
    if to_port:
        e["toPort"] = to_port
    return e



edges = [
    # Access band: horizontal flow toward the gateway, then to auth and the edge.
    edge("e-web-gw", "web-portal", "api-gateway", "straight", "HTTPS", "#2E75B6"),
    edge("e-mobile-gw", "mobile-app", "api-gateway", "straight", "gRPC", "#2E75B6"),
    edge("e-gw-auth", "api-gateway", "auth-service", "straight", "verify token", "#BF8F00", 0.012),
    edge("e-auth-cdn", "auth-service", "cdn-edge", "straight", "session", "#999999", 0.010, 2),

    # Access -> service: the gateway sits directly above the ingest service.
    edge("e-gw-ingest", "api-gateway", "ingest-service", "straight", "write API", "#507E32"),

    # Service pipeline: neighbours only, so every edge is a short horizontal run.
    edge("e-ingest-query", "ingest-service", "query-service", "rightAngle", "dataset", "#70AD47", 0.012),
    edge("e-query-report", "query-service", "report-engine", "straight", "aggregate", "#70AD47", 0.012),
    edge("e-report-sched", "report-engine", "scheduler", "straight", "schedule", "#70AD47", 0.012),
    edge("e-sched-bus", "scheduler", "event-bus", "straight", "publish", "#BF8F00", 0.012),

    # Service -> data: each store sits under its owner, so these are vertical.
    edge("e-ingest-pg", "ingest-service", "postgresql", "straight", "SQL", "#843C0C"),
    edge("e-query-redis", "query-service", "redis-cache", "straight", "cache", "#7F0000", 0.010),
    edge("e-report-lake", "report-engine", "data-lake", "straight", "Parquet", "#843C0C", 0.012),
    {**edge("e-auth-audit", "auth-service", "audit-log", "straight", "login event",
            "#999999", 0.010, 2), "labelPosition": 0.18},

    edge("e-bus-prom", "event-bus", "prometheus", "straight", "metrics", "#7030A0", 0.010, 2),

]

model = {
    "id": "enterprise-analytics-platform",
    "name": "企业智能分析平台架构图",
    "type": "architecture",
    "modelVersion": "1.0",
    "pageSize": {"width": 21.5, "height": 11.5, "unit": "inches"},
    "orientation": "landscape",
    "background": "#FFFFFF",
    "style": {"font": "Microsoft YaHei", "fontSize": 11, "palette": "enterprise",
              "gridSize": 0.25, "snapToGrid": True},
    "pages": [{
        "id": "page-0",
        "name": "Platform Architecture",
        "size": {"width": 21.5, "height": 11.5, "unit": "inches"},
        "layers": [
            {"id": "edge", "name": "Edge Layer", "visible": True},
            {"id": "app", "name": "Application Layer", "visible": True},
            {"id": "data", "name": "Data Layer", "visible": True},
        ],
        "groups": [
            {"id": "grp_access", "name": "接入层  Access Layer", "kind": "container",
             "autoSize": True,
             "style": {"fill": "#EAF3FB", "stroke": "#9DC3E6", "textColor": "#1F4E79",
                       "fontSize": 12, "bold": True, "hAlign": "left"}},
            {"id": "grp_services", "name": "服务层  Service Layer", "kind": "container",
             "autoSize": True,
             "style": {"fill": "#EDF7ED", "stroke": "#A9D08E", "textColor": "#375623",
                       "fontSize": 12, "bold": True, "hAlign": "left"}},
            {"id": "grp_data", "name": "数据层  Data Layer", "kind": "container",
             "autoSize": True,
             "style": {"fill": "#FDF2E9", "stroke": "#F4B183", "textColor": "#833C0B",
                       "fontSize": 12, "bold": True, "hAlign": "left"}},
        ],
        "nodes": nodes,
        "edges": edges,
    }],
}

with open("examples/architecture.json", "w", encoding="utf-8") as f:
    json.dump(model, f, ensure_ascii=False, indent=2)
print(f"wrote {len(nodes)} nodes, {len(edges)} edges")
