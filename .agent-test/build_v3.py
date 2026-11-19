import json

PW, PH = 11.69, 8.27
W, H = 2.30, 0.95
C = {1: 1.80, 2: 4.50, 3: 7.20, 4: 9.90}
CY = {"access": 6.45, "svc": 4.00, "data": 1.35}
BH = 1.80
BAND = {  # (centre y, height) —— 高度 1.80 = 标题 0.46 + 余隙 0.12 + 节点 0.95 + 下边距 0.22
    "access": (6.45, BH), "svc": (4.00, BH), "data": (1.35, BH),
}

def n(nid, text, shape, cx, cy, fill, txt="#FFFFFF", bold=True, fs=11, group=None):
    d = {"id": nid, "text": text, "shape": shape,
         "bounds": {"x": cx, "y": cy, "w": W, "h": H},
         "style": {"fill": fill, "stroke": "#3F3F3F", "textColor": txt, "fontSize": fs, "bold": bold}}
    if group: d["groupId"] = group
    return d

nodes = [
    n("web",     "Web 门户\nReact SPA",    "rounded", C[1], CY["access"], "#4472C4", group="grp-access"),
    n("gateway", "API 网关\nKong",         "hexagon", C[2], CY["access"], "#ED7D31", group="grp-access"),
    n("app",     "手机 App\niOS / Android", "rounded", C[3], CY["access"], "#4472C4", group="grp-access"),

    n("pay",     "支付服务\nPayment",       "rounded", C[1], CY["svc"], "#70AD47", group="grp-svc"),
    n("order",   "订单服务\nOrder",         "rounded", C[2], CY["svc"], "#548235", group="grp-svc"),
    n("stock",   "库存服务\nInventory",     "rounded", C[3], CY["svc"], "#70AD47", group="grp-svc"),

    n("redis",   "Redis\n缓存",             "process", C[1], CY["data"], "#C00000", group="grp-data"),
    n("pg",      "PostgreSQL\n订单数据",     "process", C[2], CY["data"], "#C55A11", group="grp-data"),
    n("kafka",   "Kafka\n事件总线",          "parallelogram", C[3], CY["data"], "#7030A0", group="grp-data"),
    n("analytics","下游分析系统\nBI / 报表",  "rectangle", C[4], CY["data"], "#7F7F7F", group="grp-data"),
]

BW = 11.00
groups = [
    {"id": "grp-access", "name": "接入层  Access Layer", "kind": "container", "autoSize": False,
     "bounds": {"x": 5.85, "y": BAND["access"][0], "w": BW, "h": BH},
     "style": {"fill": "#EAF3FB", "stroke": "#9DC3E6", "textColor": "#1F4E79", "fontSize": 12, "bold": True, "hAlign": "left"}},
    {"id": "grp-svc", "name": "业务服务层  Service Layer", "kind": "container", "autoSize": False,
     "bounds": {"x": 5.85, "y": BAND["svc"][0], "w": BW, "h": BH},
     "style": {"fill": "#EDF7ED", "stroke": "#A9D08E", "textColor": "#375623", "fontSize": 12, "bold": True, "hAlign": "left"}},
    {"id": "grp-data", "name": "数据与消息层  Data & Messaging", "kind": "container", "autoSize": False,
     "bounds": {"x": 5.85, "y": BAND["data"][0], "w": BW, "h": BH},
     "style": {"fill": "#F2E9F7", "stroke": "#C5A3D9", "textColor": "#4B1F6F", "fontSize": 12, "bold": True, "hAlign": "left"}},
]

def e(eid, f, t, label, color="#595959", wp=None, lp=None):
    d = {"id": eid, "from": f, "to": t, "kind": "straight", "label": label,
         "lineColor": color, "lineWeight": 0.013, "endArrow": "triangle"}
    if wp: d["waypoints"] = wp
    if lp is not None: d["labelPosition"] = lp
    return d

# 走廊 y=2.67：服务带底 3.10 与数据带顶 2.25 之间
CORR = 2.67
edges = [
    e("e-web-gw",  "web",   "gateway", "HTTPS", "#2E75B6"),
    e("e-app-gw",  "app",   "gateway", "HTTPS", "#2E75B6"),
    e("e-gw-order","gateway","order",  "REST / 下单", "#BF8F00"),

    e("e-order-pay",  "order","pay",   "扣款",   "#507E32"),
    e("e-order-stock","order","stock", "锁定库存", "#507E32"),

    e("e-order-pg",   "order","pg",    "订单数据", "#843C0C", lp=0.28),
    e("e-order-redis","order","redis", "缓存",    "#7F0000", lp=0.28),

    # 从 pay 底边竖直下到走廊，再水平右行进入 Kafka —— 不再向左折返
    e("e-pay-kafka",  "pay","kafka",   "支付事件", "#4B1F6F",
      wp=[{"x": C[1], "y": CORR}, {"x": 6.35, "y": CORR}], lp=0.16),
    e("e-order-kafka","order","kafka", "订单事件", "#4B1F6F"),
    e("e-stock-kafka","stock","kafka", "库存事件", "#4B1F6F", lp=0.62),
    e("e-kafka-ana",  "kafka","analytics","消费",  "#4B1F6F"),
]

model = {
    "id": "order-system-architecture",
    "name": "订单系统运作架构图",
    "type": "architecture",
    "modelVersion": "1.0",
    "pageSize": {"width": PW, "height": PH, "unit": "inches"},
    "orientation": "landscape",
    "background": "#FFFFFF",
    "style": {"font": "Microsoft YaHei", "fontSize": 11},
    "pages": [{
        "id": "page-1", "name": "订单系统架构",
        "size": {"width": PW, "height": PH, "unit": "inches"},
        "groups": groups,
        "nodes": nodes,
        "textBlocks": [
            {"id": "title", "text": "订单系统运作架构", "role": "title",
             "bounds": {"x": PW/2, "y": 7.82, "w": 9.0, "h": 0.36},
             "align": "center",
             "style": {"fontSize": 20, "bold": True, "textColor": "#1F3864"}},
            {"id": "subtitle", "text": "三层架构 · 统一事件总线 · 上下游解耦", "role": "subtitle",
             "bounds": {"x": PW/2, "y": 7.50, "w": 9.0, "h": 0.26},
             "align": "center",
             "style": {"fontSize": 12, "textColor": "#595959"}},
        ],
        "edges": edges,
    }],
}
json.dump(model, open("v3.model.json","w"), ensure_ascii=False, indent=2)
print("ok")
