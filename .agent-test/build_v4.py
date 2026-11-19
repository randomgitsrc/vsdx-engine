import json

PW, PH = 11.69, 8.27
W, H = 2.30, 0.95
C = {1: 1.80, 2: 4.50, 3: 7.20, 4: 9.90}

# 从 DiagramLayout 常量反推（ContainerHeading .46 / HeadingClearance .12 / PaddingBottom .22）
HEADING, CLEAR, PADB = 0.46, 0.12, 0.22
BH = HEADING + CLEAR + H + PADB          # 1.75
BAND_TOP = {"access": 7.30, "svc": 4.85, "data": 2.40}
BAND_CY  = {k: v - BH/2 for k, v in BAND_TOP.items()}
# 节点中心：底边 + 下边距 + 半高
NODE_CY  = {k: (v - BH) + PADB + H/2 for k, v in BAND_TOP.items()}

def n(nid, text, shape, cx, cy, fill, txt="#FFFFFF", bold=True, fs=11, group=None):
    d = {"id": nid, "text": text, "shape": shape,
         "bounds": {"x": cx, "y": cy, "w": W, "h": H},
         "style": {"fill": fill, "stroke": "#3F3F3F", "textColor": txt, "fontSize": fs, "bold": bold}}
    if group: d["groupId"] = group
    return d

nodes = [
    n("web",     "Web 门户\nReact SPA",    "rounded", C[1], NODE_CY["access"], "#4472C4", group="grp-access"),
    n("gateway", "API 网关\nKong",         "hexagon", C[2], NODE_CY["access"], "#ED7D31", group="grp-access"),
    n("app",     "手机 App\niOS / Android", "rounded", C[3], NODE_CY["access"], "#4472C4", group="grp-access"),

    n("pay",     "支付服务\nPayment",       "rounded", C[1], NODE_CY["svc"], "#70AD47", group="grp-svc"),
    n("order",   "订单服务\nOrder",         "rounded", C[2], NODE_CY["svc"], "#548235", group="grp-svc"),
    n("stock",   "库存服务\nInventory",     "rounded", C[3], NODE_CY["svc"], "#70AD47", group="grp-svc"),

    n("redis",   "Redis\n缓存",             "process", C[1], NODE_CY["data"], "#C00000", group="grp-data"),
    n("pg",      "PostgreSQL\n订单数据",     "process", C[2], NODE_CY["data"], "#C55A11", group="grp-data"),
    n("kafka",   "Kafka\n事件总线",          "parallelogram", C[3], NODE_CY["data"], "#7030A0", group="grp-data"),
    n("analytics","下游分析系统\nBI / 报表",  "rectangle", C[4], NODE_CY["data"], "#7F7F7F", group="grp-data"),
]

BW = 11.00
def band(gid, name, top, fill, stroke, txt):
    return {"id": gid, "name": name, "kind": "container", "autoSize": False,
            "bounds": {"x": 5.85, "y": top - BH/2, "w": BW, "h": BH},
            "style": {"fill": fill, "stroke": stroke, "textColor": txt, "fontSize": 12,
                      "bold": True, "hAlign": "left"}}

groups = [
    band("grp-access", "接入层  Access Layer",           BAND_TOP["access"], "#EAF3FB", "#9DC3E6", "#1F4E79"),
    band("grp-svc",    "业务服务层  Service Layer",       BAND_TOP["svc"],    "#EDF7ED", "#A9D08E", "#375623"),
    band("grp-data",   "数据与消息层  Data & Messaging",  BAND_TOP["data"],   "#F2E9F7", "#C5A3D9", "#4B1F6F"),
]

def e(eid, f, t, label, color="#595959", wp=None, lp=None):
    d = {"id": eid, "from": f, "to": t, "kind": "straight", "label": label,
         "lineColor": color, "lineWeight": 0.013, "endArrow": "triangle"}
    if wp: d["waypoints"] = wp
    if lp is not None: d["labelPosition"] = lp
    return d

CORR = 2.62   # 服务带底 3.10 与数据带顶 2.40 之间的走廊
edges = [
    e("e-web-gw",  "web",   "gateway", "HTTPS", "#2E75B6"),
    e("e-app-gw",  "app",   "gateway", "HTTPS", "#2E75B6"),
    e("e-gw-order","gateway","order",  "REST / 下单", "#BF8F00"),

    e("e-order-pay",  "order","pay",   "扣款",   "#507E32"),
    e("e-order-stock","order","stock", "锁定库存", "#507E32"),

    e("e-order-pg",   "order","pg",    "订单数据", "#843C0C", lp=0.24),
    e("e-order-redis","order","redis", "缓存",    "#7F0000", lp=0.24),

    e("e-pay-kafka",  "pay","kafka",   "支付事件", "#4B1F6F",
      wp=[{"x": C[1], "y": CORR}, {"x": 6.30, "y": CORR}], lp=0.12),
    e("e-order-kafka","order","kafka", "订单事件", "#4B1F6F"),
    e("e-stock-kafka","stock","kafka", "库存事件", "#4B1F6F", lp=0.66),
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
        "groups": groups, "nodes": nodes,
        "textBlocks": [
            {"id": "title", "text": "订单系统运作架构", "role": "title",
             "bounds": {"x": PW/2, "y": 7.86, "w": 9.0, "h": 0.34},
             "align": "center", "style": {"fontSize": 20, "bold": True, "textColor": "#1F3864"}},
            {"id": "subtitle", "text": "三层架构 · 统一事件总线 · 上下游解耦", "role": "subtitle",
             "bounds": {"x": PW/2, "y": 7.56, "w": 9.0, "h": 0.24},
             "align": "center", "style": {"fontSize": 11, "textColor": "#595959"}},
        ],
        "edges": edges,
    }],
}
json.dump(model, open("v4.model.json","w"), ensure_ascii=False, indent=2)
print("BH", BH, "NODE_CY", {k: round(v,3) for k,v in NODE_CY.items()})
