#!/usr/bin/env python3
"""Generates the benchmark corpus.

Six diagram families, each at three complexities, plus a set of fault-injection cases that
deliberately break a rule so the detector can be shown to catch it.

Design intent per family (the brief's §8):
  architecture   layered bands, cross-band dependencies
  microservices  service topology with a message bus
  workflow       decisions, branches, a cancel path
  network        zones, trust boundaries, device hierarchy
  org            strict hierarchy with sibling alignment
  hybrid         mixed: bands *and* a hub-and-spoke cluster

Run:  python3 tools/make_benchmarks.py
Out:  benchmarks/<family>/<case>.json
"""
import json
import os

OUT = "benchmarks"


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------
def node(nid, text, fill="#2E75B6", x=3.0, y=6.0, shape="rounded", w=2.2, h=0.95,
         group=None, bold=True, size=11, text_color="#FFFFFF", extra=None):
    n = {
        "id": nid,
        "name": nid,
        "type": "component",
        "text": text,
        "shape": shape,
        "bounds": {"x": x, "y": y, "w": w, "h": h},
        "style": {"fill": fill, "textColor": text_color, "fontSize": size, "bold": bold},
    }
    if group:
        n["groupId"] = group
    if extra:
        n.update(extra)
    return n


def edge(eid, frm, to, label=None, kind="straight", color="#507E32", pattern=None):
    e = {"id": eid, "from": frm, "to": to, "kind": kind, "lineColor": color,
         "lineWeight": 0.012, "endArrow": "triangle"}
    if label:
        e["label"] = label
    if pattern:
        e["linePattern"] = pattern
    return e


def group(gid, name, fill, stroke, text_color):
    return {
        "id": gid,
        "name": name,
        "kind": "container",
        "autoSize": True,
        "style": {"fill": fill, "stroke": stroke, "textColor": text_color,
                  "fontSize": 12, "bold": True, "hAlign": "left"},
    }


def diagram(did, name, dtype, width, height, groups, nodes, edges):
    return {
        "id": did,
        "name": name,
        "type": dtype,
        "modelVersion": "1.0",
        "pageSize": {"width": width, "height": height, "unit": "inches"},
        "orientation": "landscape" if width >= height else "portrait",
        "background": "#FFFFFF",
        "style": {"font": "Microsoft YaHei", "fontSize": 11, "snapToGrid": True},
        "pages": [{
            "id": "page-0",
            "name": name,
            "size": {"width": width, "height": height, "unit": "inches"},
            "groups": groups,
            "nodes": nodes,
            "edges": edges,
        }],
    }


def write(family, case, model):
    d = os.path.join(OUT, family)
    os.makedirs(d, exist_ok=True)
    path = os.path.join(d, f"{case}.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(model, f, ensure_ascii=False, indent=2)
    return path


# ---------------------------------------------------------------------------
# 1. architecture — layered bands
# ---------------------------------------------------------------------------
ACC = ("#EAF3FB", "#9DC3E6", "#1F4E79")
SVC = ("#EDF7ED", "#A9D08E", "#375623")
DAT = ("#FDF2E9", "#F4B183", "#833C0B")


def architecture(size):
    g = [group("grp_acc", "接入层  Access", *ACC),
         group("grp_svc", "服务层  Service", *SVC),
         group("grp_dat", "数据层  Data", *DAT)]

    if size == "simple":
        n = [
            node("web", "Web 门户\nWeb", "#2E75B6", 3.0, 9.2, group="grp_acc"),
            node("api", "API 网关", "#F2B705", 6.0, 9.2, group="grp_acc", text_color="#3F2E00", shape="hexagon"),
            node("svc", "订单服务\nOrder", "#70AD47", 4.5, 6.2, group="grp_svc"),
            node("db", "PostgreSQL", "#C55A11", 4.5, 3.2, group="grp_dat", shape="process"),
        ]
        e = [edge("e1", "web", "api", "HTTPS"), edge("e2", "api", "svc", "REST"),
             edge("e3", "svc", "db", "SQL", color="#843C0C")]
        return diagram("arch-simple", "分层架构（简单）", "architecture", 11, 12, g, n, e)

    if size == "medium":
        n = [
            node("web", "Web 门户", "#2E75B6", 2.6, 9.4, group="grp_acc"),
            node("mobile", "移动端", "#2E75B6", 2.6, 8.0, group="grp_acc"),
            node("api", "API 网关", "#F2B705", 5.6, 9.4, group="grp_acc", text_color="#3F2E00", shape="hexagon"),
            node("auth", "认证服务", "#5B9BD5", 8.6, 9.4, group="grp_acc"),
            node("ingest", "接入服务", "#70AD47", 2.6, 6.2, group="grp_svc"),
            node("order", "订单服务", "#70AD47", 5.6, 6.2, group="grp_svc"),
            node("report", "报表服务", "#70AD47", 8.6, 6.2, group="grp_svc"),
            node("pg", "PostgreSQL", "#C55A11", 3.0, 3.0, group="grp_dat", shape="process"),
            node("redis", "Redis", "#C00000", 5.8, 3.0, group="grp_dat", shape="circle", w=1.1, h=1.1, size=10),
            node("lake", "数据湖", "#ED7D31", 8.6, 3.0, group="grp_dat", shape="parallelogram"),
        ]
        e = [
            edge("e1", "web", "api", "HTTPS"), edge("e2", "mobile", "api", "gRPC"),
            edge("e3", "api", "auth", "verify"), edge("e4", "api", "order", "REST"),
            edge("e5", "api", "ingest", "REST"),
            edge("e6", "ingest", "order", "dataset"), edge("e7", "order", "report", "event"),
            edge("e8", "order", "pg", "SQL", color="#843C0C"),
            edge("e9", "order", "redis", "cache", color="#7F0000"),
            edge("e10", "report", "lake", "Parquet", color="#843C0C"),
        ]
        return diagram("arch-medium", "分层架构（中等）", "architecture", 13, 12.5, g, n, e)

    # complex: more nodes, longer labels, cross-band dependencies
    n = [
        node("web", "Web 门户\nReact SPA", "#2E75B6", 2.6, 9.6, group="grp_acc"),
        node("mobile", "移动端\niOS / Android", "#2E75B6", 2.6, 8.2, group="grp_acc"),
        node("api", "API 网关\nKong", "#F2B705", 5.6, 9.6, group="grp_acc", text_color="#3F2E00", shape="hexagon"),
        node("auth", "认证服务\nOAuth2 / JWT", "#5B9BD5", 8.6, 9.6, group="grp_acc"),
        node("cdn", "CDN", "#8FAADC", 11.2, 9.6, group="grp_acc", shape="circle", w=1.1, h=1.1, size=10),
        node("ingest", "数据接入服务\nIngest Service", "#70AD47", 2.6, 6.3, group="grp_svc"),
        node("order", "订单服务\nOrder Service", "#70AD47", 5.6, 6.3, group="grp_svc"),
        node("report", "报表引擎\nReport Engine", "#70AD47", 8.6, 6.3, group="grp_svc"),
        node("sched", "任务调度\nScheduler", "#A9D18E", 11.2, 6.3, group="grp_svc",
             shape="process", text_color="#243B14", bold=False),
        node("bus", "事件总线\nKafka", "#FFC000", 13.8, 6.3, group="grp_svc",
             shape="parallelogram", text_color="#3F2E00"),
        node("pg", "PostgreSQL\n主数据仓库", "#C55A11", 2.6, 3.0, group="grp_dat", shape="process"),
        node("redis", "Redis\n缓存", "#C00000", 5.6, 3.0, group="grp_dat", shape="circle", w=1.1, h=1.1, size=10),
        node("audit", "审计存储\nAppend-only", "#ED7D31", 8.6, 3.0, group="grp_dat", shape="data"),
        node("lake", "数据湖\nS3 / Parquet", "#ED7D31", 11.2, 3.0, group="grp_dat", shape="parallelogram"),
        node("prom", "监控告警\nPrometheus", "#7030A0", 13.8, 3.0, group="grp_dat", shape="hexagon", size=10),
    ]
    e = [
        edge("e1", "web", "api", "HTTPS"), edge("e2", "mobile", "api", "gRPC"),
        edge("e3", "api", "auth", "verify token"), edge("e4", "auth", "cdn", "session",
                                                        color="#999999", pattern=2),
        edge("e5", "api", "ingest", "write API"),
        edge("e6", "ingest", "order", "dataset"), edge("e7", "order", "report", "aggregate"),
        edge("e8", "report", "sched", "schedule"), edge("e9", "sched", "bus", "publish",
                                                         color="#BF8F00"),
        edge("e10", "order", "pg", "SQL", color="#843C0C"),
        edge("e11", "order", "redis", "cache", color="#7F0000"),
        edge("e12", "report", "audit", "archive", color="#843C0C"),
        edge("e13", "sched", "lake", "Parquet", color="#843C0C"),
        edge("e14", "bus", "prom", "metrics", color="#7030A0", pattern=2),
    ]
    return diagram("arch-complex", "分层架构（复杂）", "architecture", 16, 13, g, n, e)


# ---------------------------------------------------------------------------
# 2. microservices
# ---------------------------------------------------------------------------
def microservices(size):
    g = [group("grp_core", "核心服务  Core Services", *SVC),
         group("grp_plat", "平台设施  Platform", *ACC)]
    count = {"simple": 3, "medium": 5, "complex": 7}[size]
    names = ["订单服务\nOrder", "支付服务\nPayment", "库存服务\nInventory", "用户服务\nUser",
             "通知服务\nNotify", "搜索服务\nSearch", "推荐服务\nRecommend"]
    fills = ["#70AD47", "#2E75B6", "#C55A11", "#7030A0", "#F2B705", "#5B9BD5", "#ED7D31"]

    n, e = [], []
    for i in range(count):
        n.append(node(f"svc{i}", names[i], fills[i], 2.6 + i * 2.9, 6.4, group="grp_core",
                      size=10 if size == "complex" else 11,
                      text_color="#3F2E00" if fills[i] in ("#F2B705",) else "#FFFFFF"))
    n.append(node("gw", "API 网关", "#F2B705", 2.6 + (count - 1) * 2.9 / 2, 9.4,
                  group="grp_plat", shape="hexagon", text_color="#3F2E00", w=2.4))
    n.append(node("bus", "事件总线", "#FFC000", 2.6 + (count - 1) * 2.9 / 2, 3.2,
                  group="grp_plat", shape="parallelogram", text_color="#3F2E00", w=2.4))

    for i in range(count):
        e.append(edge(f"c{i}", "gw", f"svc{i}", None if i else "REST"))
    for i in range(count - 1):
        e.append(edge(f"s{i}", f"svc{i}", f"svc{i+1}", "event", color="#BF8F00", pattern=2))
    e.append(edge("b0", "svc0", "bus", "publish", color="#BF8F00"))
    if count > 2:
        e.append(edge("b1", f"svc{count-1}", "bus", "consume", color="#BF8F00", pattern=2))

    w = max(13, 2.6 + (count - 1) * 2.9 + 2.6)
    return diagram(f"micro-{size}", f"微服务拓扑（{size}）", "microservices", w, 12.5, g, n, e)


# ---------------------------------------------------------------------------
# 3. workflow — decisions and branches
# ---------------------------------------------------------------------------
def workflow(size):
    g = [group("grp_in", "受理  Intake", *ACC),
         group("grp_rev", "审核  Review", *DAT),
         group("grp_ful", "履约  Fulfilment", *SVC)]

    if size == "simple":
        n = [
            node("start", "提交\nStart", "#2E75B6", 3.0, 9.4, group="grp_in", shape="circle", w=1.0, h=1.0, size=9),
            node("check", "校验\nCheck", "#F2B705", 6.2, 9.4, group="grp_rev", shape="decision",
                 text_color="#3F2E00", w=2.0, h=1.2),
            node("ok", "完成\nDone", "#375623", 9.6, 9.4, group="grp_ful", shape="circle", w=1.0, h=1.0, size=9),
            node("bad", "拒绝\nReject", "#C00000", 6.2, 6.4, group="grp_rev", w=2.0, h=0.9),
        ]
        e = [edge("e1", "start", "check", None, color="#2E75B6"),
             edge("e2", "check", "ok", "通过", color="#507E32"),
             edge("e3", "check", "bad", "不通过", kind="rightAngle", color="#C00000")]
        return diagram("flow-simple", "业务流程（简单）", "workflow", 12, 11.5, g, n, e)

    if size == "medium":
        n = [
            node("start", "提交订单", "#2E75B6", 3.0, 9.6, group="grp_in", shape="circle", w=1.0, h=1.0, size=9),
            node("valid", "校验订单", "#5B9BD5", 6.0, 9.6, group="grp_in", shape="process", w=2.1, h=0.9),
            node("stock", "库存检查", "#F2B705", 9.2, 9.6, group="grp_rev", shape="decision",
                 text_color="#3F2E00", w=2.0, h=1.2),
            node("pay", "支付授权", "#F2B705", 12.4, 9.6, group="grp_rev", text_color="#3F2E00", w=2.2, h=0.9),
            node("reject", "拒绝订单", "#C00000", 9.2, 7.0, group="grp_rev", w=2.0, h=0.9),
            node("pick", "拣货包装", "#70AD47", 3.0, 3.6, group="grp_ful", w=2.2, h=0.9),
            node("ship", "发货", "#70AD47", 6.2, 3.6, group="grp_ful", shape="process", w=2.1, h=0.9),
            node("done", "完成", "#375623", 9.4, 3.6, group="grp_ful", shape="circle", w=1.0, h=1.0, size=9),
        ]
        e = [
            edge("e1", "start", "valid", None, color="#2E75B6"),
            edge("e2", "valid", "stock", None, color="#2E75B6"),
            edge("e3", "stock", "pay", "有货", color="#507E32"),
            edge("e4", "stock", "reject", "缺货", kind="rightAngle", color="#C00000"),
            edge("e5", "pay", "pick", "已授权", kind="rightAngle", color="#507E32"),
            edge("e6", "pick", "ship", None, color="#507E32"),
            edge("e7", "ship", "done", None, color="#507E32"),
            edge("e8", "reject", "done", "取消", kind="rightAngle", color="#999999", pattern=2),
        ]
        return diagram("flow-medium", "业务流程（中等）", "workflow", 15, 12.5, g, n, e)

    # complex: extra branch, parallel path, longer labels
    n = [
        node("start", "订单提交\nOrder placed", "#2E75B6", 3.0, 10.0, group="grp_in",
             shape="circle", w=1.1, h=1.1, size=9),
        node("valid", "校验订单\nValidate", "#5B9BD5", 6.2, 10.0, group="grp_in", shape="process"),
        node("risk", "风控评估\nRisk check", "#7030A0", 9.6, 10.0, group="grp_rev", shape="decision",
             w=2.0, h=1.2),
        node("stock", "库存检查\nStock check", "#F2B705", 13.0, 10.0, group="grp_rev",
             shape="decision", text_color="#3F2E00", w=2.0, h=1.2),
        node("pay", "支付授权\nAuthorise", "#F2B705", 16.4, 10.0, group="grp_rev",
             text_color="#3F2E00"),
        node("reject", "拒绝订单\nReject", "#C00000", 9.6, 7.2, group="grp_rev", w=2.0, h=0.9),
        node("manual", "人工复核\nManual review", "#ED7D31", 13.0, 7.2, group="grp_rev", w=2.4),
        node("pick", "拣货包装\nPick & pack", "#70AD47", 3.0, 3.6, group="grp_ful"),
        node("ship", "发货\nShip", "#70AD47", 6.4, 3.6, group="grp_ful", shape="process"),
        node("notify", "通知客户\nNotify", "#70AD47", 9.8, 3.6, group="grp_ful"),
        node("done", "完成\nDone", "#375623", 13.0, 3.6, group="grp_ful",
             shape="circle", w=1.1, h=1.1, size=9),
    ]
    e = [
        edge("e1", "start", "valid", None, color="#2E75B6"),
        edge("e2", "valid", "risk", None, color="#2E75B6"),
        edge("e3", "risk", "stock", "通过", color="#507E32"),
        edge("e4", "risk", "reject", "高风险", kind="rightAngle", color="#C00000"),
        edge("e5", "stock", "pay", "有货", color="#507E32"),
        edge("e6", "stock", "manual", "缺货", kind="rightAngle", color="#ED7D31"),
        edge("e7", "manual", "pay", "补货完成", kind="rightAngle", color="#ED7D31"),
        edge("e8", "pay", "pick", "已授权", kind="rightAngle", color="#507E32"),
        edge("e9", "pick", "ship", None, color="#507E32"),
        edge("e10", "ship", "notify", None, color="#507E32"),
        edge("e11", "notify", "done", None, color="#507E32"),
        edge("e12", "reject", "done", "取消", kind="rightAngle", color="#999999", pattern=2),
    ]
    return diagram("flow-complex", "业务流程（复杂）", "workflow", 19, 13.5, g, n, e)


# ---------------------------------------------------------------------------
# 4. network
# ---------------------------------------------------------------------------
def network(size):
    g = [group("grp_edge", "边缘区  Edge", *DAT),
         group("grp_core", "核心区  Core", *ACC),
         group("grp_sec", "安全域  Security", "#F2E9F7", "#C5A3D9", "#4B1F6F")]

    if size == "simple":
        n = [
            node("net", "互联网", "#D3D3D3", 3.0, 9.2, group="grp_edge", shape="cloud",
                 text_color="#111111"),
            node("fw", "防火墙", "#C00000", 6.4, 9.2, group="grp_edge", shape="hexagon"),
            node("sw", "交换机", "#2E75B6", 4.6, 6.2, group="grp_core", shape="process"),
            node("app", "应用服务器", "#70AD47", 4.6, 3.2, group="grp_core"),
        ]
        e = [edge("n1", "net", "fw", "WAN", color="#808080"),
             edge("n2", "fw", "sw", "trunk"),
             edge("n3", "sw", "app", "VLAN 10")]
        return diagram("net-simple", "网络拓扑（简单）", "network", 11, 12, g, n, e)

    if size == "medium":
        n = [
            node("net", "互联网", "#D3D3D3", 2.6, 9.4, group="grp_edge", shape="cloud", text_color="#111111"),
            node("fw", "防火墙", "#C00000", 5.8, 9.4, group="grp_edge", shape="hexagon"),
            node("lb", "负载均衡", "#ED7D31", 9.0, 9.4, group="grp_edge", shape="process"),
            node("dmz", "DMZ 交换机", "#5B9BD5", 2.6, 6.2, group="grp_core", shape="process"),
            node("core", "核心交换机", "#2E75B6", 5.8, 6.2, group="grp_core", shape="process"),
            node("appsw", "应用区交换机", "#5B9BD5", 9.0, 6.2, group="grp_core", shape="process"),
            node("ids", "入侵检测", "#7030A0", 12.2, 6.2, group="grp_sec", shape="hexagon"),
            node("a1", "应用服务器 A", "#70AD47", 3.0, 3.2, group="grp_core"),
            node("a2", "应用服务器 B", "#70AD47", 6.4, 3.2, group="grp_core"),
            node("db", "数据库集群", "#C55A11", 9.8, 3.2, group="grp_core", shape="process"),
        ]
        e = [edge("n1", "net", "fw", "WAN", color="#808080"),
             edge("n2", "fw", "lb", "DMZ", color="#C00000"),
             edge("n3", "lb", "appsw", "10G"),
             edge("n4", "fw", "core", "trunk"),
             edge("n5", "core", "dmz", None),
             edge("n6", "core", "appsw", None),
             edge("n7", "appsw", "ids", "镜像", color="#7030A0", pattern=2),
             edge("n8", "dmz", "a1", "VLAN 20"),
             edge("n9", "core", "a2", "VLAN 21"),
             edge("n10", "appsw", "db", "VLAN 30", color="#843C0C")]
        return diagram("net-medium", "网络拓扑（中等）", "network", 14.5, 12.5, g, n, e)

    n = [
        node("net", "互联网\nInternet", "#D3D3D3", 2.6, 9.8, group="grp_edge", shape="cloud", text_color="#111111"),
        node("fw", "边界防火墙\nFirewall", "#C00000", 5.8, 9.8, group="grp_edge", shape="hexagon"),
        node("waf", "WAF", "#C55A11", 9.0, 9.8, group="grp_edge", shape="hexagon", size=10),
        node("lb", "负载均衡\nLoad balancer", "#ED7D31", 12.2, 9.8, group="grp_edge", shape="process"),
        node("dmz", "DMZ 交换机", "#5B9BD5", 2.6, 6.4, group="grp_core", shape="process"),
        node("core", "核心交换机\nCore switch", "#2E75B6", 5.8, 6.4, group="grp_core", shape="process"),
        node("appsw", "应用区交换机", "#5B9BD5", 9.0, 6.4, group="grp_core", shape="process"),
        node("dbs", "数据区交换机", "#5B9BD5", 12.2, 6.4, group="grp_core", shape="process"),
        node("ids", "入侵检测\nIDS", "#7030A0", 15.4, 6.4, group="grp_sec", shape="hexagon", size=10),
        node("a1", "应用服务器 A\n10.20.1.11", "#70AD47", 2.6, 3.2, group="grp_core", size=10),
        node("a2", "应用服务器 B\n10.20.1.12", "#70AD47", 5.8, 3.2, group="grp_core", size=10),
        node("db", "数据库集群\n10.20.2.0/24", "#C55A11", 9.0, 3.2, group="grp_core", shape="process", size=10),
        node("bak", "备份存储\nOffsite", "#808080", 12.2, 3.2, group="grp_sec", shape="data", size=10),
        node("mon", "监控采集\nPrometheus", "#7030A0", 15.4, 3.2, group="grp_sec", shape="hexagon", size=10),
    ]
    e = [edge("n1", "net", "fw", "WAN", color="#808080"),
         edge("n2", "fw", "waf", "DMZ", color="#C00000"),
         edge("n3", "waf", "lb", "清洗后"),
         edge("n4", "lb", "appsw", "10G"),
         edge("n5", "fw", "core", "trunk"),
         edge("n6", "core", "dmz", None),
         edge("n7", "core", "appsw", None),
         edge("n8", "appsw", "dbs", "VLAN 30"),
         edge("n9", "dbs", "ids", "镜像", color="#7030A0", pattern=2),
         edge("n10", "dmz", "a1", "VLAN 20"),
         edge("n11", "core", "a2", "VLAN 21"),
         edge("n12", "dbs", "db", "VLAN 30", color="#843C0C"),
         edge("n13", "ids", "bak", "syslog", color="#999999", pattern=2),
         edge("n14", "dbs", "mon", "SNMP", color="#7030A0", pattern=2)]
    return diagram("net-complex", "网络拓扑（复杂）", "network", 18, 13, g, n, e)


# ---------------------------------------------------------------------------
# 5. org chart
# ---------------------------------------------------------------------------
def orgchart(size):
    g = [group("grp_exec", "决策层  Executive", *DAT),
         group("grp_dept", "部门  Departments", *ACC)]

    if size == "simple":
        n = [
            node("ceo", "CEO", "#C55A11", 6.0, 9.4, group="grp_exec", w=2.0),
            node("cto", "CTO", "#2E75B6", 4.0, 6.4, group="grp_dept", w=2.0),
            node("cfo", "CFO", "#2E75B6", 8.0, 6.4, group="grp_dept", w=2.0),
        ]
        e = [edge("o1", "ceo", "cto"), edge("o2", "ceo", "cfo")]
        return diagram("org-simple", "组织结构（简单）", "org", 12, 12, g, n, e)

    if size == "medium":
        n = [
            node("ceo", "CEO", "#C55A11", 7.0, 9.6, group="grp_exec", w=2.0),
            node("cto", "CTO", "#2E75B6", 3.4, 6.6, group="grp_dept", w=2.0),
            node("cfo", "CFO", "#2E75B6", 6.0, 6.6, group="grp_dept", w=2.0),
            node("coo", "COO", "#2E75B6", 8.6, 6.6, group="grp_dept", w=2.0),
            node("dev", "研发部", "#70AD47", 3.4, 3.4, group="grp_dept"),
            node("qa", "质量部", "#70AD47", 6.0, 3.4, group="grp_dept"),
            node("fin", "财务部", "#70AD47", 8.6, 3.4, group="grp_dept"),
        ]
        e = [edge("o1", "ceo", "cto"), edge("o2", "ceo", "cfo"), edge("o3", "ceo", "coo"),
             edge("o4", "cto", "dev"), edge("o5", "cto", "qa"), edge("o6", "cfo", "fin")]
        return diagram("org-medium", "组织结构（中等）", "org", 13, 12.5, g, n, e)

    n = [
        node("ceo", "CEO\n首席执行官", "#C55A11", 8.0, 9.8, group="grp_exec", w=2.4),
        node("cto", "CTO\n技术", "#2E75B6", 3.2, 7.0, group="grp_dept", w=2.0, size=10),
        node("cfo", "CFO\n财务", "#2E75B6", 5.8, 7.0, group="grp_dept", w=2.0, size=10),
        node("coo", "COO\n运营", "#2E75B6", 8.4, 7.0, group="grp_dept", w=2.0, size=10),
        node("chro", "CHRO\n人力", "#2E75B6", 11.0, 7.0, group="grp_dept", w=2.0, size=10),
        node("dev", "研发部", "#70AD47", 2.0, 4.0, group="grp_dept", w=1.9, size=10),
        node("qa", "质量部", "#70AD47", 4.4, 4.0, group="grp_dept", w=1.9, size=10),
        node("fin", "财务部", "#70AD47", 6.8, 4.0, group="grp_dept", w=1.9, size=10),
        node("ops", "运营部", "#70AD47", 9.2, 4.0, group="grp_dept", w=1.9, size=10),
        node("hr", "人力资源部", "#70AD47", 11.6, 4.0, group="grp_dept", w=2.2, size=10),
        node("plat", "平台组", "#A9D18E", 2.0, 1.6, group="grp_dept", w=1.9, size=10, text_color="#243B14"),
        node("biz", "业务组", "#A9D18E", 4.4, 1.6, group="grp_dept", w=1.9, size=10, text_color="#243B14"),
    ]
    e = [edge("o1", "ceo", "cto"), edge("o2", "ceo", "cfo"), edge("o3", "ceo", "coo"),
         edge("o4", "ceo", "chro"), edge("o5", "cto", "dev"), edge("o6", "cto", "qa"),
         edge("o7", "cfo", "fin"), edge("o8", "coo", "ops"), edge("o9", "chro", "hr"),
         edge("o10", "dev", "plat"), edge("o11", "dev", "biz")]
    return diagram("org-complex", "组织结构（复杂）", "org", 16, 13, g, n, e)


# ---------------------------------------------------------------------------
# 6. hybrid — bands plus a hub-and-spoke cluster
# ---------------------------------------------------------------------------
def hybrid(size):
    g = [group("grp_front", "前端  Frontend", *ACC),
         group("grp_back", "后端  Backend", *SVC),
         group("grp_infra", "基础设施  Infrastructure", *DAT)]

    if size == "simple":
        n = [
            node("web", "Web", "#2E75B6", 3.4, 9.4, group="grp_front"),
            node("api", "API", "#70AD47", 6.6, 9.4, group="grp_back"),
            node("db", "DB", "#C55A11", 6.6, 6.4, group="grp_infra", shape="process"),
            node("cache", "Cache", "#C00000", 9.4, 6.4, group="grp_infra", shape="circle", w=1.1, h=1.1, size=10),
        ]
        e = [edge("h1", "web", "api", "REST"), edge("h2", "api", "db", "SQL", color="#843C0C"),
             edge("h3", "api", "cache", "cache", color="#7F0000")]
        return diagram("hybrid-simple", "混合架构（简单）", "architecture", 12, 12, g, n, e)

    if size == "medium":
        n = [
            node("web", "Web", "#2E75B6", 2.6, 9.6, group="grp_front"),
            node("mobile", "Mobile", "#2E75B6", 2.6, 8.2, group="grp_front"),
            node("api", "API 网关", "#F2B705", 5.6, 9.6, group="grp_back", shape="hexagon", text_color="#3F2E00"),
            node("svc", "业务服务", "#70AD47", 8.6, 9.6, group="grp_back"),
            node("worker", "异步 Worker", "#A9D18E", 8.6, 7.0, group="grp_back",
                 shape="process", text_color="#243B14", bold=False),
            node("db", "PostgreSQL", "#C55A11", 3.4, 4.0, group="grp_infra", shape="process"),
            node("cache", "Redis", "#C00000", 5.8, 4.0, group="grp_infra", shape="circle", w=1.1, h=1.1, size=10),
            node("mq", "消息队列", "#FFC000", 8.6, 4.0, group="grp_infra", shape="parallelogram", text_color="#3F2E00"),
        ]
        e = [edge("h1", "web", "api", "HTTPS"), edge("h2", "mobile", "api", "gRPC"),
             edge("h3", "api", "svc", "REST"),
             edge("h4", "svc", "db", "SQL", color="#843C0C"),
             edge("h5", "svc", "cache", "cache", color="#7F0000"),
             edge("h6", "svc", "mq", "publish", color="#BF8F00"),
             edge("h7", "mq", "worker", "consume", color="#BF8F00", pattern=2),
             edge("h8", "worker", "db", "write", color="#843C0C", pattern=2)]
        return diagram("hybrid-medium", "混合架构（中等）", "architecture", 14, 12.5, g, n, e)

    n = [
        node("web", "Web 门户\nReact", "#2E75B6", 2.6, 10.0, group="grp_front"),
        node("mobile", "移动端\niOS/Android", "#2E75B6", 2.6, 8.6, group="grp_front"),
        node("admin", "管理后台\nAdmin", "#2E75B6", 2.6, 7.2, group="grp_front"),
        node("api", "API 网关\nKong", "#F2B705", 5.8, 10.0, group="grp_back", shape="hexagon", text_color="#3F2E00"),
        node("auth", "认证服务", "#5B9BD5", 9.0, 10.0, group="grp_back"),
        node("order", "订单服务", "#70AD47", 5.8, 7.2, group="grp_back"),
        node("report", "报表服务", "#70AD47", 9.0, 7.2, group="grp_back"),
        node("worker", "异步 Worker", "#A9D18E", 12.2, 7.2, group="grp_back",
             shape="process", text_color="#243B14", bold=False),
        node("pg", "PostgreSQL\n主库", "#C55A11", 2.6, 3.4, group="grp_infra", shape="process"),
        node("redis", "Redis\n缓存", "#C00000", 5.4, 3.4, group="grp_infra", shape="circle", w=1.1, h=1.1, size=10),
        node("mq", "Kafka\n事件总线", "#FFC000", 8.6, 3.4, group="grp_infra",
             shape="parallelogram", text_color="#3F2E00"),
        node("lake", "数据湖\nS3", "#ED7D31", 11.8, 3.4, group="grp_infra", shape="parallelogram"),
        node("mon", "监控\nGrafana", "#7030A0", 14.6, 3.4, group="grp_infra", shape="hexagon", size=10),
    ]
    e = [edge("h1", "web", "api", "HTTPS"), edge("h2", "mobile", "api", "gRPC"),
         edge("h3", "admin", "api", "HTTPS"),
         edge("h4", "api", "auth", "verify"),
         edge("h5", "api", "order", "REST"),
         edge("h6", "api", "report", "REST"),
         edge("h7", "order", "pg", "SQL", color="#843C0C"),
         edge("h8", "order", "redis", "cache", color="#7F0000"),
         edge("h9", "order", "mq", "publish", color="#BF8F00"),
         edge("h10", "mq", "worker", "consume", color="#BF8F00", pattern=2),
         edge("h11", "worker", "lake", "Parquet", color="#843C0C"),
         edge("h12", "report", "lake", "read", color="#843C0C", pattern=2),
         edge("h13", "mq", "mon", "metrics", color="#7030A0", pattern=2)]
    return diagram("hybrid-complex", "混合架构（复杂）", "architecture", 17.5, 13.5, g, n, e)


# ---------------------------------------------------------------------------
# fault injection — deliberately break one rule each
# ---------------------------------------------------------------------------
def faults():
    cases = {}

    # 1. overlapping nodes
    cases["fault-overlap"] = {
        "id": "fault-overlap", "name": "故障：节点重叠", "type": "architecture",
        "pageSize": {"width": 12, "height": 8, "unit": "inches"},
        "pages": [{
            "id": "p", "name": "Fault", "size": {"width": 12, "height": 8, "unit": "inches"},
            "groups": [], "edges": [],
            "nodes": [
                node("a", "节点 A", "#2E75B6", 4.0, 5.0, w=2.0, h=1.0),
                node("b", "节点 B", "#70AD47", 4.6, 5.0, w=2.0, h=1.0),
                node("c", "节点 C", "#C55A11", 8.0, 5.0, w=2.0, h=1.0),
            ],
        }],
    }

    # 2. container smaller than its members
    cases["fault-container-too-small"] = {
        "id": "fault-container-too-small", "name": "故障：容器过小", "type": "architecture",
        "pageSize": {"width": 12, "height": 8, "unit": "inches"},
        "pages": [{
            "id": "p", "name": "Fault", "size": {"width": 12, "height": 8, "unit": "inches"},
            "groups": [{"id": "g", "name": "小容器", "kind": "container", "autoSize": False,
                        "bounds": {"x": 5.0, "y": 5.0, "w": 2.0, "h": 1.0},
                        "style": {"fill": "#EAF3FB", "stroke": "#9DC3E6", "bold": True, "hAlign": "left"}}],
            "nodes": [
                node("a", "远在左", "#2E75B6", 2.5, 5.0, group="g"),
                node("b", "远在右", "#70AD47", 8.5, 5.0, group="g"),
            ],
            "edges": [],
        }],
    }

    # 3. very long labels in small boxes
    cases["fault-long-text"] = {
        "id": "fault-long-text", "name": "故障：长文字", "type": "architecture",
        "pageSize": {"width": 12, "height": 8, "unit": "inches"},
        "pages": [{
            "id": "p", "name": "Fault", "size": {"width": 12, "height": 8, "unit": "inches"},
            "groups": [], "edges": [],
            "nodes": [
                node("a", "这是一个非常长的节点标题用于测试文字溢出",
                     "#2E75B6", 3.0, 5.0, w=1.5, h=0.5),
                node("b", "An extremely long English caption that will not fit",
                     "#70AD47", 7.0, 5.0, w=1.5, h=0.5),
            ],
        }],
    }

    # 4. crossing connectors
    cases["fault-crossing"] = {
        "id": "fault-crossing", "name": "故障：连线交叉", "type": "architecture",
        "pageSize": {"width": 12, "height": 9, "unit": "inches"},
        "pages": [{
            "id": "p", "name": "Fault", "size": {"width": 12, "height": 9, "unit": "inches"},
            "groups": [], "nodes": [
                node("tl", "左上", "#2E75B6", 3.0, 7.0, w=1.8, h=0.8),
                node("tr", "右上", "#5B9BD5", 9.0, 7.0, w=1.8, h=0.8),
                node("bl", "左下", "#70AD47", 3.0, 3.0, w=1.8, h=0.8),
                node("br", "右下", "#C55A11", 9.0, 3.0, w=1.8, h=0.8),
            ],
            "edges": [
                edge("x1", "tl", "br", None, color="#2E75B6"),
                edge("x2", "tr", "bl", None, color="#C00000"),
            ],
        }],
    }

    # 5. many cross-band connectors through unrelated nodes
    cases["fault-cross-region"] = {
        "id": "fault-cross-region", "name": "故障：跨区穿线", "type": "architecture",
        "pageSize": {"width": 14, "height": 9, "unit": "inches"},
        "pages": [{
            "id": "p", "name": "Fault", "size": {"width": 14, "height": 9, "unit": "inches"},
            "groups": [], "nodes": [
                node("l", "起点", "#2E75B6", 2.0, 5.0, w=1.6, h=0.8),
                node("m1", "障碍 1", "#808080", 5.0, 5.0, w=1.6, h=0.8),
                node("m2", "障碍 2", "#808080", 7.5, 5.0, w=1.6, h=0.8),
                node("r", "终点", "#70AD47", 11.0, 5.0, w=1.6, h=0.8),
            ],
            "edges": [edge("x1", "l", "r", "穿过", color="#C00000")],
        }],
    }

    # 6. mixed node sizes that break row rhythm
    cases["fault-size-mix"] = {
        "id": "fault-size-mix", "name": "故障：尺寸混杂", "type": "architecture",
        "pageSize": {"width": 14, "height": 9, "unit": "inches"},
        "pages": [{
            "id": "p", "name": "Fault", "size": {"width": 14, "height": 9, "unit": "inches"},
            "groups": [], "edges": [],
            "nodes": [
                node("a", "很小", "#2E75B6", 2.5, 5.0, w=0.8, h=0.6),
                node("b", "非常大", "#70AD47", 6.0, 5.0, w=4.5, h=1.6),
                node("c", "中等", "#C55A11", 10.0, 5.0, w=2.0, h=0.9),
            ],
        }],
    }

    # 7. misaligned row
    cases["fault-misaligned"] = {
        "id": "fault-misaligned", "name": "故障：行错位", "type": "architecture",
        "pageSize": {"width": 14, "height": 9, "unit": "inches"},
        "pages": [{
            "id": "p", "name": "Fault", "size": {"width": 14, "height": 9, "unit": "inches"},
            "groups": [], "edges": [],
            "nodes": [
                node("a", "节点 A", "#2E75B6", 3.0, 5.12, w=2.0, h=0.9),
                node("b", "节点 B", "#70AD47", 6.3, 4.90, w=2.0, h=0.9),
                node("c", "节点 C", "#C55A11", 9.6, 5.16, w=2.0, h=0.9),
            ],
        }],
    }

    # 8. node outside the page
    cases["fault-out-of-page"] = {
        "id": "fault-out-of-page", "name": "故障：越出画布", "type": "architecture",
        "pageSize": {"width": 10, "height": 7, "unit": "inches"},
        "pages": [{
            "id": "p", "name": "Fault", "size": {"width": 10, "height": 7, "unit": "inches"},
            "groups": [], "edges": [],
            "nodes": [
                node("a", "在页面内", "#2E75B6", 3.0, 4.0, w=2.0, h=0.9),
                node("b", "超出右边界", "#C00000", 11.0, 4.0, w=2.0, h=0.9),
                node("c", "超出上边界", "#C00000", 3.0, 8.0, w=2.0, h=0.9),
            ],
        }],
    }

    return cases


def main():
    made = []
    for size in ("simple", "medium", "complex"):
        made.append(write("architecture", size, architecture(size)))
        made.append(write("microservices", size, microservices(size)))
        made.append(write("workflow", size, workflow(size)))
        made.append(write("network", size, network(size)))
        made.append(write("org", size, orgchart(size)))
        made.append(write("hybrid", size, hybrid(size)))

    for name, model in faults().items():
        made.append(write("faults", name, model))

    print(f"wrote {len(made)} benchmark models under {OUT}/")


if __name__ == "__main__":
    main()
