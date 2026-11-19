import json

PW, PH = 11.69, 8.27          # A4 landscape
W, H = 2.30, 0.95
X = [1.80, 4.50, 7.20, 9.90]  # 4 columns
Y = {"access": 6.00, "svc": 3.55, "data": 1.10}

def n(nid, text, shape, x, y, fill, txt="#FFFFFF", bold=True, fs=11, group=None):
    d = {"id": nid, "text": text, "shape": shape,
         "bounds": {"x": x, "y": y, "w": W, "h": H},
         "style": {"fill": fill, "textColor": txt, "fontSize": fs, "bold": bold}}
    if group: d["groupId"] = group
    return d

nodes = [
    # 接入层
    n("web",      "Web 门户\nReact SPA",  "rounded",  X[0], Y["access"], "#4472C4", group="grp-access"),
    n("gateway",  "API 网关\nKong",       "hexagon",  X[1], Y["access"], "#ED7D31", group="grp-access"),
    n("app",      "手机 App\niOS / Android","rounded", X[2], Y["access"], "#4472C4", group="grp-access"),
    # 业务层
    n("pay",      "支付服务\nPayment",    "rounded",  X[0], Y["svc"],    "#70AD47", group="grp-svc"),
    n("order",    "订单服务\nOrder",      "rounded",  X[1], Y["svc"],    "#548235", group="grp-svc"),
    n("stock",    "库存服务\nInventory",  "rounded",  X[2], Y["svc"],    "#70AD47", group="grp-svc"),
    # 数据与消息层
    n("pg",       "PostgreSQL\n订单数据",  "process",  X[0], Y["data"],   "#C55A11", group="grp-data"),
    n("redis",    "Redis\n缓存",          "process",  X[1], Y["data"],   "#C00000", group="grp-data"),
    n("kafka",    "Kafka\n事件总线",       "parallelogram", X[2], Y["data"], "#7030A0", group="grp-data"),
    n("analytics","下游分析系统\nBI / 报表","rectangle", X[3], Y["data"],  "#7F7F7F", group="grp-data"),
]

groups = [
    {"id": "grp-access", "name": "接入层  Access Layer", "kind": "container", "autoSize": True,
     "style": {"fill": "#EAF3FB", "stroke": "#9DC3E6", "textColor": "#1F4E79", "fontSize": 12, "bold": True, "hAlign": "left"}},
    {"id": "grp-svc", "name": "业务服务层  Service Layer", "kind": "container", "autoSize": True,
     "style": {"fill": "#EDF7ED", "stroke": "#A9D08E", "textColor": "#375623", "fontSize": 12, "bold": True, "hAlign": "left"}},
    {"id": "grp-data", "name": "数据与消息层  Data & Messaging", "kind": "container", "autoSize": True,
     "style": {"fill": "#F2E9F7", "stroke": "#C5A3D9", "textColor": "#4B1F6F", "fontSize": 12, "bold": True, "hAlign": "left"}},
]

def e(eid, f, t, label, color="#595959"):
    return {"id": eid, "from": f, "to": t, "kind": "straight", "label": label,
            "lineColor": color, "lineWeight": 0.013, "endArrow": "triangle"}

edges = [
    e("e-web-gw",   "web",   "gateway", "HTTPS", "#2E75B6"),
    e("e-app-gw",   "app",   "gateway", "HTTPS", "#2E75B6"),
    e("e-gw-order", "gateway","order",   "REST",  "#BF8F00"),
    e("e-gw-pay",   "gateway","pay",     "REST",  "#BF8F00"),
    e("e-gw-stock", "gateway","stock",   "REST",  "#BF8F00"),
    e("e-order-pay",  "order","pay",     "扣款",  "#507E32"),
    e("e-order-stock","order","stock",   "锁定库存","#507E32"),
    e("e-order-pg",   "order","pg",      "订单数据","#843C0C"),
    e("e-order-redis","order","redis",   "缓存",  "#7F0000"),
    e("e-order-kafka","order","kafka",   "订单事件","#4B1F6F"),
    e("e-pay-kafka",  "pay",  "kafka",   "支付事件","#4B1F6F"),
    e("e-stock-kafka","stock","kafka",   "库存事件","#4B1F6F"),
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
             "box": {"x": PW/2, "y": 7.55, "width": 9.0, "height": 0.44},
             "align": "center", "bold": True, "color": "#1F3864"}
        ],
        "edges": edges,
    }],
}
json.dump(model, open("v1.model.json","w"), ensure_ascii=False, indent=2)
print("written", len(nodes), "nodes", len(edges), "edges")
