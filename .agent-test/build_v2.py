import json

PW, PH = 11.69, 8.27
W, H = 2.30, 0.95
C = {1: 1.90, 2: 4.60, 3: 7.30, 4: 10.00}
Y = {"access": 6.10, "svc": 3.55, "data": 1.15}

def n(nid, text, shape, cx, cy, fill, txt="#FFFFFF", bold=True, fs=11, group=None, w=W, h=H):
    d = {"id": nid, "text": text, "shape": shape,
         "bounds": {"x": cx, "y": cy, "w": w, "h": h},
         "style": {"fill": fill, "stroke": "#404040", "textColor": txt, "fontSize": fs, "bold": bold}}
    if group: d["groupId"] = group
    return d

nodes = [
    n("web",     "Web 门户\nReact SPA",     "rounded", C[1], Y["access"], "#4472C4", group="grp-access"),
    n("gateway", "API 网关\nKong",          "hexagon", C[2], Y["access"], "#ED7D31", group="grp-access"),
    n("app",     "手机 App\niOS / Android",  "rounded", C[3], Y["access"], "#4472C4", group="grp-access"),

    n("pay",     "支付服务\nPayment",        "rounded", C[1], Y["svc"], "#70AD47", group="grp-svc"),
    n("order",   "订单服务\nOrder",          "rounded", C[2], Y["svc"], "#548235", group="grp-svc"),
    n("stock",   "库存服务\nInventory",      "rounded", C[3], Y["svc"], "#70AD47", group="grp-svc"),

    n("redis",   "Redis\n缓存",              "process", C[1], Y["data"], "#C00000", group="grp-data"),
    n("pg",      "PostgreSQL\n订单数据",      "process", C[2], Y["data"], "#C55A11", group="grp-data"),
    n("kafka",   "Kafka\n事件总线",           "parallelogram", C[3], Y["data"], "#7030A0", group="grp-data"),
    n("analytics","下游分析系统\nBI / 报表",   "rectangle", C[4], Y["data"], "#7F7F7F", group="grp-data"),
]

# 统一带宽：三条带同 x 同宽，视觉上成为对齐的水平层
BW, BH, BCX = 11.00, 1.75, 5.85
groups = [
    {"id": "grp-access", "name": "接入层  Access Layer", "kind": "container", "autoSize": False,
     "bounds": {"x": BCX, "y": 6.28, "w": BW, "h": BH},
     "style": {"fill": "#EAF3FB", "stroke": "#9DC3E6", "textColor": "#1F4E79", "fontSize": 12, "bold": True, "hAlign": "left"}},
    {"id": "grp-svc", "name": "业务服务层  Service Layer", "kind": "container", "autoSize": False,
     "bounds": {"x": BCX, "y": 3.73, "w": BW, "h": BH},
     "style": {"fill": "#EDF7ED", "stroke": "#A9D08E", "textColor": "#375623", "fontSize": 12, "bold": True, "hAlign": "left"}},
    {"id": "grp-data", "name": "数据与消息层  Data & Messaging", "kind": "container", "autoSize": False,
     "bounds": {"x": BCX, "y": 1.33, "w": BW, "h": BH},
     "style": {"fill": "#F2E9F7", "stroke": "#C5A3D9", "textColor": "#4B1F6F", "fontSize": 12, "bold": True, "hAlign": "left"}},
]

def e(eid, f, t, label, color="#595959", wp=None, lp=None):
    d = {"id": eid, "from": f, "to": t, "kind": "straight", "label": label,
         "lineColor": color, "lineWeight": 0.013, "endArrow": "triangle"}
    if wp: d["waypoints"] = wp
    if lp is not None: d["labelPosition"] = lp
    return d

edges = [
    e("e-web-gw",  "web",   "gateway", "HTTPS", "#2E75B6"),
    e("e-app-gw",  "app",   "gateway", "HTTPS", "#2E75B6"),
    e("e-gw-order","gateway","order",  "REST / 下单", "#BF8F00"),

    e("e-order-pay",  "order","pay",   "扣款", "#507E32"),
    e("e-order-stock","order","stock", "锁定库存", "#507E32"),

    e("e-order-pg",   "order","pg",    "订单数据", "#843C0C"),
    e("e-order-redis","order","redis", "缓存", "#7F0000"),

    # 走带间空隙（y=2.52）再进入 Kafka，避免直连穿过 Redis
    e("e-pay-kafka",  "pay","kafka",   "支付事件", "#4B1F6F",
      wp=[{"x": 1.90, "y": 2.52}, {"x": 6.50, "y": 2.52}]),
    e("e-order-kafka","order","kafka", "订单事件", "#4B1F6F"),
    e("e-stock-kafka","stock","kafka", "库存事件", "#4B1F6F"),
    e("e-kafka-ana",  "kafka","analytics","消费", "#4B1F6F"),
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
             "bounds": {"x": PW/2, "y": 7.78, "w": 9.0, "h": 0.38},
             "align": "center",
             "style": {"fontSize": 20, "bold": True, "textColor": "#1F3864"}}
        ],
        "edges": edges,
    }],
}
json.dump(model, open("v2.model.json","w"), ensure_ascii=False, indent=2)
print("ok")
