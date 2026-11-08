/* Labstage prototype, built only from the Labstage design system v2 (window.Labstage). Sample data is fictional. */
(function () {
  "use strict";
  var h = React.createElement, useState = React.useState, useEffect = React.useEffect, useRef = React.useRef;
  var L = window.Labstage, egp = L.egp, ORDER = L.STAGES;
  var NAME = { design: "Design", milling: "Milling", layering: "Layering", glaze: "Glaze" };
  var PRICE = { "Zirconia crown": 1800, "E.max veneer": 2100, "PFM crown": 1500, "Implant crown": 3600, "Zirconia bridge unit": 1800 };
  var ME_DOCTOR = "Dr. Karim Adel", ME_LAB = "Nile Precision Lab";

  /* ---------- data ---------- */
  function C(id, patient, doctor, lab, work, units, teeth, shade, stage, due, o) {
    var c = Object.assign({ id: id, patient: patient, doctor: doctor, lab: lab, workType: work, units: units, work: units + " × " + work, teeth: teeth, shade: shade, stage: stage, stages: ORDER, due: due, paidHalves: 0 }, o || {});
    c.total = Math.round(PRICE[work] * units * 1.05);
    return c;
  }
  var CASES0 = [
    C("LS-24817", "Mona Hassan", ME_DOCTOR, ME_LAB, "Zirconia crown", 2, "16, 26", "A2", "layering", "Thu 30 Oct", { paidHalves: 1, dates: { design: "Tue 21 Oct", milling: "Sat 25 Oct", layering: "Since 11:40" } }),
    C("LS-24790", "Youssef Samir", ME_DOCTOR, ME_LAB, "E.max veneer", 2, "11, 21", "B1", "design", "Mon 27 Oct", { waiting: "approve", dates: { design: "File sent 09:40" } }),
    C("LS-24802", "Salma Nabil", ME_DOCTOR, "Delta Smile Lab", "Implant crown", 1, "36", "A3", "design", "Wed 29 Oct", { waiting: "pay", dates: { design: "Approved 08:30" } }),
    C("LS-24811", "Omar Fathy", ME_DOCTOR, "Cairo Ceramic Studio", "Zirconia bridge unit", 3, "44–46", "A3", "glaze", "Today", { stages: ["design", "milling", "glaze"], paidHalves: 1, dates: { design: "Thu 16 Oct", milling: "Mon 20 Oct", glaze: "Ready 07:55" } }),
    C("LS-24795", "Hana Ezzat", ME_DOCTOR, "Delta Smile Lab", "PFM crown", 1, "24", "A3.5", "milling", "Tue 28 Oct", { paidHalves: 1, dates: { design: "Fri 24 Oct", milling: "Since 10:00" } }),
    C("LS-24768", "Ahmed Lotfy", ME_DOCTOR, "Cairo Ceramic Studio", "Zirconia crown", 1, "47", "A2", "milling", "Sat 25 Oct", { overdue: true, stages: ["design", "milling", "glaze"], paidHalves: 1, dates: { design: "Mon 20 Oct", milling: "Since Thu" } }),
    C("LS-24826", "Laila Sabry", "Dr. Nadia Fouad", ME_LAB, "Implant crown", 1, "46", "A2", "design", "Wed 29 Oct", { dates: { design: "Due Mon" } }),
    C("LS-24828", "Kareem Hosny", "Dr. Omar Rashed", ME_LAB, "Zirconia bridge unit", 3, "14–16", "A3", "milling", "Fri 31 Oct", { paidHalves: 1, dates: { design: "Thu 23 Oct", milling: "Sintering, out 18:00" } }),
    C("LS-24821", "Nour Galal", "Dr. Laila Mahmoud", ME_LAB, "PFM crown", 1, "25", "A3.5", "layering", "Thu 30 Oct", { paidHalves: 1, dates: { design: "Wed 22 Oct", milling: "Fri 24 Oct", layering: "Since 09:15" } }),
    C("LS-24812", "Sherif Anwar", "Dr. Omar Rashed", ME_LAB, "Zirconia crown", 1, "36", "A2", "glaze", "Today", { paidHalves: 1, dates: { design: "Sat 18 Oct", milling: "Tue 21 Oct", layering: "Thu 23 Oct", glaze: "Courier 14:00" } })
  ];
  var INCOMING0 = [
    C("LS-24833", "Rana Mostafa", ME_DOCTOR, ME_LAB, "Zirconia crown", 2, "16, 26", "A2", "design", "Tue 4 Nov", { pickup: "Pickup today 12:00–2:00 pm" }),
    C("LS-24834", "Tarek Ibrahim", "Dr. Nadia Fouad", ME_LAB, "E.max veneer", 4, "12–22", "B1", "design", "Wed 5 Nov", { stages: ["design", "milling", "glaze"], pickup: "Scan uploaded, no pickup" })
  ];
  var LABS = [
    { name: "Nile Precision Lab", area: "Dokki, Giza", km: 3.2, kit: "5-axis mill, intraoral scanner", rating: 4.8, reviews: 126, from: 1200, days: 3, sponsored: true },
    { name: "Delta Smile Lab", area: "Mohandessin, Giza", km: 4.1, kit: "Press furnace, 4-axis mill", rating: 4.7, reviews: 84, from: 950, days: 4 },
    { name: "Cairo Ceramic Studio", area: "Agouza, Giza", km: 5.6, kit: "Layering ceramics, implant work", rating: 4.6, reviews: 61, from: 1400, days: 5 },
    { name: "October Zircon Lab", area: "6th of October, Giza", km: 18.4, kit: "5-axis mill, sintering furnace", rating: 4.5, reviews: 40, from: 900, days: 4 }
  ];
  var INVOICES = [
    { doctor: "Dr. Karim Adel", clinic: "Dokki Dental Clinic", orders: 9, billed: 21600, paid: 16200 },
    { doctor: "Dr. Nadia Fouad", clinic: "Zamalek Smile Centre", orders: 6, billed: 15300, paid: 15300 },
    { doctor: "Dr. Omar Rashed", clinic: "Rashed Dental, Mohandessin", orders: 5, billed: 12600, paid: 6300 },
    { doctor: "Dr. Laila Mahmoud", clinic: "Agouza Family Dentistry", orders: 4, billed: 7200, paid: 7200 },
    { doctor: "Dr. Hesham Saleh", clinic: "Saleh Clinic, 6th of October", orders: 2, billed: 3600, paid: 1800 }
  ];
  var NOTES0 = {
    doctor: [
      { id: 1, kind: "stage", title: "LS-24817 moved to Layering", text: "Nile Precision Lab", time: "11:40", unread: true, go: "case/LS-24817", cat: "Stages" },
      { id: 2, kind: "payment", title: "First half due on LS-24802", text: "EGP 1,890 after the design was approved", time: "09:05", unread: true, go: "case/LS-24802", cat: "Payments" },
      { id: 3, kind: "chat", title: "New file on LS-24790", text: "LS-24790_design.stl · Nile Precision Lab", time: "09:40", unread: true, go: "case/LS-24790", cat: "Chat" },
      { id: 4, kind: "stage", title: "LS-24811 is ready", text: "Cairo Ceramic Studio · courier at 2:00 pm", time: "07:55", go: "case/LS-24811", cat: "Stages" },
      { id: 5, kind: "pickup", title: "Courier collected LS-24795", text: "Delta Smile Lab · Fri 24 Oct", time: "Fri", go: "case/LS-24795", cat: "Stages" }
    ],
    lab: [
      { id: 1, kind: "order", title: "New order LS-24833", text: "Dr. Karim Adel · 2 × Zirconia crown", time: "12:01", unread: true, go: "board", cat: "Orders" },
      { id: 2, kind: "order", title: "New order LS-24834", text: "Dr. Nadia Fouad · 4 × E.max veneer", time: "11:20", unread: true, go: "board", cat: "Orders" },
      { id: 3, kind: "chat", title: "Dr. Karim Adel wrote on LS-24817", text: "Please open the contact on 26 slightly.", time: "10:31", go: "case/LS-24817", cat: "Chat" },
      { id: 4, kind: "payment", title: "Dr. Omar Rashed paid the first half of LS-24828", text: "EGP 2,835", time: "Thu", go: "invoices", cat: "Payments" }
    ]
  };
  function messagesFor(c, role) {
    var other = role === "doctor" ? c.lab : c.doctor, m = [];
    var i = c.stages.indexOf(c.stage);
    m.push({ from: "system", stage: "design", text: "Order placed with " + c.lab, time: "Mon 09:14" });
    if (i > 0 || c.waiting) m.push({ from: "lab", name: c.lab, text: "Design is ready for review.", time: "Tue 10:14", file: c.id + "_design.stl · 4.2 MB" });
    if (i > 0) { m.push({ from: "doctor", name: c.doctor, text: "Approved. Please open the contact slightly.", time: "Tue 10:31" }); m.push({ from: "system", stage: c.stages[1], text: "Moved to " + NAME[c.stages[1]], time: "Tue 11:05" }); }
    if (i > 1) m.push({ from: "system", stage: c.stages[2], text: "Moved to " + NAME[c.stages[2]], time: "11:40" });
    if (i > 2) m.push({ from: "system", stage: "glaze", text: "Moved to Glaze. Second half due on delivery.", time: "07:55" });
    m.forEach(function (x) { if (x.from === "lab" && role === "lab") x.from = "lab"; });
    return m;
  }
  function paid(c) { return c.total / 2 * c.paidHalves; }

  /* ---------- small shared pieces (layout only; all controls are design-system components) ---------- */
  function Panel(p) {
    return h("section", { className: "ls-panel", "aria-labelledby": p.id, style: p.style },
      p.title ? h("header", null, h("h2", { id: p.id }, p.title), p.meta ? h("span", null, p.meta) : null) : null, p.children);
  }
  function Head(p) {
    return h("div", { className: "pg-head" }, h("div", null, h("h1", null, p.title), p.lead ? h("p", null, p.lead) : null), p.actions ? h("div", { className: "pg-acts" }, p.actions) : null);
  }
  function Grid(p) { return h("div", { className: "pg-grid" }, h("div", { className: "pg-col" }, p.main), h("div", { className: "pg-col" }, p.rail)); }

  /* ---------- order-number search (both roles) ---------- */
  function Search(p) {
    var st = useState(""), q = st[0], setQ = st[1], op = useState(false), open = op[0], setOpen = op[1];
    var pool = p.cases.concat(p.role === "lab" ? p.incoming : []);
    var t = q.trim().toLowerCase();
    var hits = t ? pool.filter(function (c) { return (c.id + " " + c.patient + " " + c.doctor + " " + c.lab).toLowerCase().indexOf(t) >= 0; }).slice(0, 6) : [];
    function submit(e) {
      e.preventDefault();
      var exact = pool.filter(function (c) { return c.id.toLowerCase() === t || c.id.toLowerCase() === "ls-" + t; })[0];
      var pick = exact || (hits.length === 1 ? hits[0] : null);
      if (pick) { p.go("case/" + pick.id); setQ(""); setOpen(false); }
      else setOpen(true);
    }
    return h("form", { className: "pg-search", role: "search", onSubmit: submit },
      h("label", { htmlFor: "q", className: "ls-sr" }, "Search cases"),
      h(L.Icon, { name: "search" }),
      h("input", { id: "q", value: q, autoComplete: "off", placeholder: p.role === "lab" ? "Order number, doctor or patient" : "Order number or patient, e.g. LS-24817", onChange: function (e) { setQ(e.target.value); setOpen(true); }, onFocus: function () { setOpen(true); }, onBlur: function () { setTimeout(function () { setOpen(false); }, 150); }, "aria-describedby": "q-res" }),
      open && t ? h("div", { id: "q-res", className: "pg-results", role: "listbox" },
        hits.length ? hits.map(function (c) {
          return h("button", { key: c.id, type: "button", role: "option", onMouseDown: function (e) { e.preventDefault(); p.go("case/" + c.id); setQ(""); setOpen(false); } },
            h("span", { className: "ls-mono" }, c.id), h("b", null, c.patient), h("span", { className: "sub" }, (p.role === "lab" ? c.doctor : c.lab) + " · " + c.work), h(L.StagePill, { stage: c.stage }));
        }) : h("p", { className: "ls-empty" }, "No case matches “" + q.trim() + "”. Order numbers look like LS-24817.")) : null);
  }

  /* ---------- doctor screens ---------- */
  function DoctorCases(p) {
    var mine = p.cases.filter(function (c) { return c.doctor === ME_DOCTOR; });
    var waiting = mine.filter(function (c) { return c.waiting; });
    var moving = mine.filter(function (c) { return c.id === "LS-24817"; })[0] || mine[0];
    var due = waiting.filter(function (c) { return c.waiting === "pay"; }).reduce(function (a, c) { return a + c.total / 2; }, 0);
    var labs = {}; mine.forEach(function (c) { labs[c.lab] = 1; });
    return h(React.Fragment, null,
      h(Head, { title: "Cases", lead: mine.length + " active with " + Object.keys(labs).length + " labs · " + waiting.length + " waiting for you", actions: h(L.Button, { variant: "primary", icon: "plus", onClick: function () { p.go("new"); } }, "New order") }),
      h(Grid, {
        main: [
          h(Panel, { key: "w", id: "wq", title: "Waiting for you", meta: due ? egp(due) + " due" : null },
            h(L.ActionQueue, { empty: "Nothing waiting for you.", items: waiting.map(function (c) {
              var pay = c.waiting === "pay";
              return { id: c.id, title: (pay ? "Pay the first half of " : "Approve the design of ") + c.id, detail: c.patient + " · " + c.work + " · " + c.lab, amount: pay ? c.total / 2 : undefined,
                actions: [{ label: pay ? "Open case" : "Open file", variant: "ghost", onClick: function () { p.go("case/" + c.id); } }, { label: pay ? "Pay" : "Approve", onClick: function () { p.act(pay ? "pay" : "approve", c.id); } }] };
            }) })),
          h(Panel, { key: "a", id: "ac", title: "All cases", meta: "Overdue first, then by due date" },
            h(L.CasesTable, { cases: mine.slice().sort(function (a, b) { return (b.overdue ? 1 : 0) - (a.overdue ? 1 : 0); }).map(function (c) { return Object.assign({}, c, { paid: paid(c) }); }), onOpen: function (c) { p.go("case/" + c.id); } }))
        ],
        rail: [
          h(Panel, { key: "m", id: "mv", title: "Moving now", meta: moving.id },
            h("div", { className: "pg-pad" },
              h("div", { className: "pg-crown ls-field-" + moving.stage }, h(L.CrownStage, { stage: moving.stage, shade: moving.shade })),
              h("p", { className: "pg-note" }, h("b", null, moving.patient + " is in " + NAME[moving.stage]), h("br"), moving.lab + " · " + (moving.dates && moving.dates[moving.stage] || "")),
              h(L.Button, { onClick: function () { p.go("case/" + moving.id); } }, "Open case"))),
          h(Panel, { key: "u", id: "up", title: "Updates", meta: h("a", { href: "#", onClick: function (e) { e.preventDefault(); p.go("notifications"); } }, "View all") },
            p.notes.slice(0, 3).map(function (n) { return h(L.Notification, Object.assign({ key: n.id }, n, { onOpen: function () { p.readNote(n); } })); }))
        ]
      }));
  }

  var UNIT = PRICE;
  function DoctorNew(p) {
    var t = useState([16, 26]), teeth = t[0], setTeeth = t[1];
    var w = useState("Zirconia crown"), work = w[0], setWork = w[1];
    var s = useState("A2"), shade = s[0], setShade = s[1];
    var g = useState({ design: true, milling: true, layering: true, glaze: true }), stages = g[0], setStages = g[1];
    var lab = p.orderLab || LABS[0];
    var err = teeth.length ? null : "Pick at least one tooth";
    return h(React.Fragment, null,
      h(Head, { title: "New order", lead: "Goes to " + lab.name + " · draft saved 12:04", actions: h(L.Button, { variant: "ghost", onClick: function () { p.go("cases"); } }, "Cancel") }),
      h(Grid, {
        main: h("form", { className: "ls-panel pg-form", onSubmit: function (e) { e.preventDefault(); } },
          h("fieldset", null, h("legend", null, "Lab"), h(L.LabRow, { lab: lab, onOrder: function () { p.go("labs"); } }), h("p", { className: "pg-hint" }, "Use Order on another lab in Find a lab to change it.")),
          h("fieldset", null, h("legend", null, "Case"),
            h("div", { className: "pg-row2" }, h(L.Field, { label: "Patient or case name", defaultValue: "Rana Mostafa" }), h(L.Field, { label: "Your reference", placeholder: "File no. 1182", hint: "Only you see it" })),
            h("div", { className: "pg-chips", role: "radiogroup", "aria-label": "Type of work" }, Object.keys(UNIT).map(function (k) {
              return h("label", { key: k, className: "pg-chip" }, h("input", { type: "radio", name: "work", checked: work === k, onChange: function () { setWork(k); } }), h("span", null, k, h("small", null, egp(UNIT[k]))));
            }))),
          h("fieldset", null, h("legend", null, "Teeth"), h(L.ToothChart, { value: teeth, onChange: setTeeth }), err ? h("p", { className: "pg-err", role: "alert" }, err) : null),
          h("fieldset", null, h("legend", null, "Shade"), h(L.ShadePicker, { value: shade, onChange: setShade })),
          h("fieldset", null, h("legend", null, "Stages"), h("div", { className: "pg-chips" }, ORDER.map(function (k) {
            return h("label", { key: k, className: "pg-chip" }, h("input", { type: "checkbox", checked: stages[k], onChange: function () { var n = Object.assign({}, stages); n[k] = !n[k]; setStages(n); } }), h("span", null, h(L.StagePill, { stage: k })));
          }))),
          h("fieldset", null, h("legend", null, "Files"),
            h("label", { className: "pg-drop", htmlFor: "up" }, h(L.Icon, { name: "attach" }), h("b", null, "Drop scans and photos, or browse"), h("span", null, "STL, PLY, JPG or PDF, up to 200 MB each"), h("input", { id: "up", type: "file", multiple: true, className: "ls-sr" })),
            h("div", { className: "pg-files" }, ["mostafa_upper_scan.stl · 18.4 MB", "shade_photo.jpg · 2.1 MB"].map(function (f) { return h("div", { key: f }, h(L.Icon, { name: "file" }), h("span", { className: "ls-mono" }, f)); })),
            h(L.Field, { as: "textarea", label: "Notes for the lab", defaultValue: "Full contour, light contact on 26. Send the design for approval before milling." })),
          h("fieldset", null, h("legend", null, "Pickup"),
            h("label", { className: "pg-chip", style: { alignSelf: "flex-start" } }, h("input", { type: "checkbox", defaultChecked: true }), h("span", null, h(L.Icon, { name: "truck" }), "Send a courier for the impression")),
            h("div", { className: "pg-row3" }, h(L.Field, { label: "Day", type: "date", defaultValue: "2026-10-27" }), h(L.Field, { label: "Time", as: "select", options: ["12:00 – 2:00 pm", "2:00 – 4:00 pm", "6:00 – 8:00 pm"] }), h(L.Field, { label: "Clinic address", defaultValue: "14 Mossadak St, Dokki, Giza" })))),
        rail: [
          h(Panel, { key: "pv", id: "pv", title: "Preview", meta: "Shade " + shade }, h("div", { className: "pg-pad" }, h("div", { className: "pg-crown ls-field-glaze" }, h(L.CrownStage, { stage: "glaze", shade: shade })))),
          h(Panel, { key: "sm", id: "sm", title: "Summary" }, h("div", { className: "pg-pad pg-stack" },
            h(L.OrderSummary, { work: work, units: teeth.length, unitPrice: UNIT[work] }),
            h("p", { className: "pg-hint" }, "The lab accepts or declines within 2 hours. Nothing is charged until the design is uploaded."),
            h(L.Button, { variant: "primary", disabled: !!err, onClick: function () { p.toast("Order sent to " + lab.name + ". You'll hear back within 2 hours."); p.go("cases"); } }, "Send order to lab")))
        ]
      }));
  }

  function CasePage(p) {
    var c = p.cases.concat(p.incoming).filter(function (x) { return x.id === p.id; })[0];
    if (!c) return h(React.Fragment, null, h(Head, { title: "Case not found", lead: "There is no case " + p.id + ". Check the order number." }), h(L.Button, { onClick: function () { p.go(p.role === "lab" ? "board" : "cases"); } }, "Back"));
    var lab = p.role === "lab", i = c.stages.indexOf(c.stage), next = c.stages[i + 1];
    var actions = lab
      ? [next ? h(L.Button, { key: "m", size: "sm", onClick: function () { p.act("move", c.id); } }, "Move to " + NAME[next]) : h(L.Button, { key: "d", size: "sm", onClick: function () { p.act("deliver", c.id); } }, "Mark delivered"),
         c.stage === "design" ? h(L.Button, { key: "u", size: "sm", icon: "attach", onClick: function () { p.toast("Choose the design file in the live app."); } }, "Upload design") : null]
      : [h(L.Button, { key: "p", size: "sm", icon: "truck", onClick: function () { p.toast("Pickup requested. " + c.lab + " will confirm the time."); } }, "Request pickup"),
         h(L.Button, { key: "i", size: "sm", onClick: function () { p.toast("The invoice PDF is generated in the live app."); } }, "Invoice PDF")];
    var half = c.total / 2, labHalf = half / 1.05 * 0.95;
    return h(React.Fragment, null,
      h("div", { className: "pg-back" }, h(L.Button, { variant: "ghost", size: "sm", onClick: function () { p.go(lab ? "board" : "cases"); } }, "← " + (lab ? "Board" : "Cases"))),
      h(L.CaseHeader, { case: c, view: lab ? "lab" : "doctor", actions: actions }),
      h("div", { style: { height: 20 } }),
      h(Grid, {
        main: [
          h(Panel, { key: "py", id: "py", title: "Payments", meta: lab ? "After the 5% Labstage fee" : "Includes the 5% Labstage fee" },
            h("div", { className: "ls-table-wrap" }, h("table", { className: "ls-table" }, h("tbody", null,
              ["First half", "Second half"].map(function (label, k) {
                var done = c.paidHalves > k, dueNow = !done && (k === 0 ? (c.waiting === "pay") : c.stage === "glaze");
                return h("tr", { key: label }, h("td", null, h("b", null, label), h("span", { className: "sub" }, k === 0 ? "After Design" : "After Glaze")),
                  h("td", null, done ? h(L.Pill, { tone: "success" }, "Paid") : dueNow ? h(L.Pill, { tone: "warn" }, "Due now") : h(L.Pill, null, "Not due yet")),
                  h("td", { className: "num" }, egp(lab ? labHalf : half)),
                  h("td", { className: "num" }, done ? h(L.Button, { variant: "ghost", size: "sm", onClick: function () { p.toast("The receipt PDF is generated in the live app."); } }, "Receipt PDF") : !lab && dueNow ? h(L.Button, { variant: "primary", size: "sm", onClick: function () { p.act("pay", c.id); } }, "Pay") : null));
              })),
              h("tfoot", null, h("tr", null, h("td", null, "Total"), h("td"), h("td", { className: "num" }, egp(lab ? labHalf * 2 : c.total)), h("td")))))),
          h(Panel, { key: "fl", id: "fl", title: "Files", meta: "Scans, photos and designs" }, h("div", { className: "pg-pad pg-files" },
            [c.patient.split(" ")[1].toLowerCase() + "_upper_scan.stl · Doctor · 18.4 MB", "shade_photo_" + c.shade + ".jpg · Doctor · 2.1 MB"].concat(i > 0 || c.waiting ? [c.id + "_design.stl · Lab · 4.2 MB"] : []).map(function (f) { return h("div", { key: f }, h(L.Icon, { name: "file" }), h("span", { className: "ls-mono" }, f)); }))),
          h(Panel, { key: "od", id: "od", title: "Order" }, h("div", { className: "pg-pad" }, h("dl", { className: "pg-dl" },
            h("dt", null, "Doctor"), h("dd", null, c.doctor), h("dt", null, "Lab"), h("dd", null, c.lab), h("dt", null, "Work"), h("dd", null, c.work), h("dt", null, "Teeth"), h("dd", { className: "ls-mono" }, c.teeth),
            h("dt", null, "Shade"), h("dd", { className: "ls-mono" }, c.shade), h("dt", null, "Stages"), h("dd", null, c.stages.map(function (s) { return NAME[s]; }).join(", ")), h("dt", null, "Due"), h("dd", null, c.due))))
        ],
        rail: h("div", { className: "pg-chat" }, h(L.CaseChat, { key: c.id, caseId: c.id, with: lab ? c.doctor : c.lab, viewer: lab ? "lab" : "doctor", messages: messagesFor(c, p.role), onAttach: function () { p.toast("Choose a scan, photo or PDF in the live app."); }, onSend: function () { p.toast((lab ? c.doctor : c.lab) + " has been told about your message."); } }))
      }));
  }

  function DoctorLabs(p) {
    var gv = useState("Giza"), gov = gv[0], setGov = gv[1], ar = useState("All areas"), area = ar[0], setArea = ar[1], so = useState("Highest rated"), sort = so[0], setSort = so[1];
    var list = LABS.filter(function (l) { return gov === "Giza" && (area === "All areas" || l.area.indexOf(area) === 0); });
    list.sort(function (a, b) { return (b.sponsored ? 1 : 0) - (a.sponsored ? 1 : 0) || (sort === "Nearest to my clinic" ? a.km - b.km : sort === "Lowest starting price" ? a.from - b.from : b.rating - a.rating); });
    return h(React.Fragment, null,
      h(Head, { title: "Find a lab", lead: list.length + " labs in " + gov + (area !== "All areas" ? ", " + area : "") + " · ranked by doctors' ratings, sponsored labs first and marked" }),
      h("div", { className: "ls-panel pg-pad pg-filters" },
        h(L.Field, { label: "Governorate", as: "select", options: ["Giza", "Cairo", "Alexandria", "Qalyubia"], value: gov, onChange: function (e) { setGov(e.target.value); } }),
        h(L.Field, { label: "Area", as: "select", options: ["All areas", "Dokki", "Mohandessin", "Agouza", "6th of October"], value: area, onChange: function (e) { setArea(e.target.value); } }),
        h(L.Field, { label: "Sort", as: "select", options: ["Highest rated", "Nearest to my clinic", "Lowest starting price"], value: sort, onChange: function (e) { setSort(e.target.value); } })),
      h("div", { style: { height: 20 } }),
      h(Grid, {
        main: h(Panel, { id: "lr", title: "Labs", meta: list.length ? null : "No labs here yet" },
          list.length ? list.map(function (l) { return h("div", { key: l.name, className: "pg-labwrap" }, h(L.LabRow, { lab: l, onOrder: function () { p.setOrderLab(l); p.go("new"); } }), h(L.Button, { variant: "ghost", size: "sm", className: "pg-profile-btn", onClick: function () { p.go("lab/" + encodeURIComponent(l.name)); } }, "View profile")); })
            : h("p", { className: "ls-empty" }, "No labs in " + gov + " yet. Try another governorate.")),
        rail: h(Panel, { id: "mp", title: "Map", meta: "Google Maps in the live app" }, h("div", { className: "pg-pad" }, h("div", { className: "pg-map", role: "img", "aria-label": "Map of labs around Dokki" },
          h("svg", { viewBox: "0 0 100 80", preserveAspectRatio: "none" }, h("path", { d: "M58 0 C54 20 60 40 52 60 C48 70 50 80 50 80", stroke: "var(--line)", strokeWidth: 5, fill: "none" }),
            [[52, 46, "NP"], [40, 38, "DS"], [61, 31, "CC"], [16, 64, "OZ"]].map(function (q) { return h("g", { key: q[2] }, h("circle", { cx: q[0], cy: q[1], r: 2.6, fill: "var(--ink)" }), h("text", { x: q[0] + 4, y: q[1] + 1.5, fontSize: 4, fill: "var(--ink)" }, q[2])); }),
            h("circle", { cx: 47, cy: 44, r: 2, fill: "var(--accent)" }), h("text", { x: 30, y: 52, fontSize: 3.6, fill: "var(--ink-2)" }, "Your clinic")))))
      }));
  }

  /* lab profile: read-only for doctors, editable for the lab */
  var PORTFOLIO = ["Zirconia bridge 44–46", "E.max veneers 12–22", "Implant crown 36", "Layered zirconia 11, 21", "Full-arch PMMA temporary", "PFM crown 24"];
  var DEVICES = [["5-axis mill", "Zirconia, PMMA, wax"], ["Intraoral scanner", "Accepts STL and PLY"], ["Sintering furnace", "Up to 1,530 °C"], ["Press furnace", "E.max press"]];
  var REVIEWS = [["Dr. Nadia Fouad", 5, "Veneers came back on time and matched B1 well."], ["Dr. Omar Rashed", 5, "Clear design files, quick to answer in the chat."], ["Dr. Hesham Saleh", 4, "Good fit. One case took a day longer than planned."]];
  function LabProfile(p) {
    var lab = LABS.filter(function (l) { return l.name === p.name; })[0] || LABS[0], own = p.role === "lab";
    return h(React.Fragment, null,
      own ? null : h("div", { className: "pg-back" }, h(L.Button, { variant: "ghost", size: "sm", onClick: function () { p.go("labs"); } }, "← Find a lab")),
      h(Head, { title: own ? "Profile" : lab.name, lead: lab.area + " · rated " + lab.rating.toFixed(1) + " from " + lab.reviews + " cases · " + lab.days + "-day turnaround",
        actions: own ? [h(L.Button, { key: "pv", onClick: function () { p.toast("This is how doctors see your profile."); } }, "Preview as a doctor"), h(L.Button, { key: "ed", variant: "primary", onClick: function () { p.toast("Profile saved. Doctors see the changes now."); } }, "Save changes")]
          : h(L.Button, { variant: "primary", onClick: function () { p.setOrderLab(lab); p.go("new"); } }, "Order from this lab") }),
      h(Grid, {
        main: [
          h(Panel, { key: "wk", id: "wk", title: "Work", meta: own ? "Add photos of finished cases" : PORTFOLIO.length + " cases" },
            h("div", { className: "pg-pad pg-portfolio" }, PORTFOLIO.map(function (t, k) { return h("figure", { key: t }, h("div", { className: "ph ls-field-" + ORDER[k % 4] }, h("span", null, "Photo")), h("figcaption", null, t)); }),
              own ? h("label", { className: "pg-drop", htmlFor: "pf" }, h(L.Icon, { name: "plus" }), h("b", null, "Add photos"), h("span", null, "JPG or PNG"), h("input", { id: "pf", type: "file", multiple: true, className: "ls-sr" })) : null)),
          h(Panel, { key: "pr", id: "pr", title: "Prices", meta: "Per unit, before the 5% Labstage fee" },
            h("div", { className: "ls-table-wrap" }, h("table", { className: "ls-table" }, h("thead", null, h("tr", null, h("th", null, "Work"), h("th", { className: "num" }, "Price"), h("th", { className: "num" }, "Turnaround"))),
              h("tbody", null, Object.keys(PRICE).map(function (k) { return h("tr", { key: k }, h("td", null, k), h("td", { className: "num" }, own ? h("input", { className: "ls-input pg-price", defaultValue: PRICE[k], "aria-label": k + " price in EGP", inputMode: "numeric" }) : egp(PRICE[k])), h("td", { className: "num" }, k === "Implant crown" ? "5 days" : "3 days")); }))))),
          h(Panel, { key: "rv", id: "rv", title: "Reviews", meta: lab.rating.toFixed(1) + " from " + lab.reviews + " rated cases" },
            REVIEWS.map(function (r) { return h("div", { key: r[0], className: "ls-qrow" }, h("div", { className: "what" }, h("b", null, r[0] + " · " + r[1] + " / 5"), h("span", null, r[2]))); }))
        ],
        rail: [
          h(Panel, { key: "dv", id: "dv", title: "Equipment" }, DEVICES.map(function (d) { return h("div", { key: d[0], className: "ls-qrow" }, h("div", { className: "what" }, h("b", null, d[0]), h("span", null, d[1]))); })),
          own ? h(Panel, { key: "pl", id: "pl", title: "Plan" }, h("div", { className: "pg-pad pg-stack" },
            h(L.KpiStrip, { items: [{ label: "Free month", value: "16 days left", note: "Ends Wed 12 Nov" }] }),
            h("p", { className: "pg-hint" }, "Then EGP 2,000 a month for the whole lab. Labstage keeps 5% of each order from your payout."),
            h(L.Button, { onClick: function () { p.toast("Payment method saved. Simulated."); } }, "Add payment method"))) :
          h(Panel, { key: "ct", id: "ct", title: "Contact" }, h("div", { className: "pg-pad pg-stack" }, h("p", { className: "pg-hint" }, "Every order has its own chat with the lab. Open a case to write to them."), h(L.Button, { onClick: function () { p.setOrderLab(lab); p.go("new"); } }, "Start an order")))
        ]
      }));
  }

  function DoctorPayments(p) {
    var mine = p.cases.filter(function (c) { return c.doctor === ME_DOCTOR; });
    var tot = mine.reduce(function (a, c) { return a + c.total; }, 0), pd = mine.reduce(function (a, c) { return a + paid(c); }, 0);
    return h(React.Fragment, null,
      h(Head, { title: "Payments", lead: "October 2026 · every payment has a PDF receipt", actions: h(L.Button, { onClick: function () { p.toast("The Excel file, with a dashboard sheet, is generated in the live app."); } }, "Export to Excel") }),
      h(L.KpiStrip, { items: [{ label: "Still to pay", value: egp(tot - pd), note: "Across " + mine.length + " cases", lead: true }, { label: "Paid", value: egp(pd) }, { label: "Labstage fees included", value: egp(tot - tot / 1.05) }] }),
      h("div", { style: { height: 20 } }),
      h(Panel, { id: "pt", title: "By case" }, h("div", { className: "ls-table-wrap" }, h("table", { className: "ls-table" },
        h("thead", null, h("tr", null, h("th", null, "Case"), h("th", null, "Lab"), h("th", { className: "num" }, "Total"), h("th", { className: "num" }, "Paid"), h("th", { className: "num" }, "Left"), h("th"))),
        h("tbody", null, mine.map(function (c) { return h("tr", { key: c.id }, h("td", null, h("button", { type: "button", className: "ls-rowlink", onClick: function () { p.go("case/" + c.id); } }, c.patient), h("span", { className: "sub ls-mono" }, c.id)), h("td", null, c.lab), h("td", { className: "num" }, egp(c.total)), h("td", { className: "num" }, egp(paid(c))), h("td", { className: "num" }, egp(c.total - paid(c))), h("td", { className: "num" }, h(L.Button, { variant: "ghost", size: "sm", onClick: function () { p.toast("The PDF is generated in the live app."); } }, "PDF"))); })),
        h("tfoot", null, h("tr", null, h("td", null, "Total"), h("td"), h("td", { className: "num" }, egp(tot)), h("td", { className: "num" }, egp(pd)), h("td", { className: "num" }, egp(tot - pd)), h("td")))))));
  }

  function Notifications(p) {
    var f = useState("All"), filter = f[0], setFilter = f[1];
    var cats = ["All", "Unread"].concat(p.role === "lab" ? ["Orders", "Chat", "Payments"] : ["Stages", "Payments", "Chat"]);
    var list = p.notes.filter(function (n) { return filter === "All" || (filter === "Unread" ? n.unread : n.cat === filter); });
    var unread = p.notes.filter(function (n) { return n.unread; }).length;
    return h(React.Fragment, null,
      h(Head, { title: "Notifications", lead: unread + " unread · every action between you and " + (p.role === "lab" ? "your doctors" : "your labs") + " lands here", actions: h(L.Button, { disabled: !unread, onClick: p.readAll }, "Mark all as read") }),
      h("div", { className: "pg-tabs", role: "tablist", "aria-label": "Filter notifications" }, cats.map(function (c) { return h("button", { key: c, type: "button", role: "tab", "aria-selected": filter === c, onClick: function () { setFilter(c); } }, c); })),
      h(Panel, { id: "nl", title: null }, list.length ? list.map(function (n) { return h(L.Notification, Object.assign({ key: n.id }, n, { onOpen: function () { p.readNote(n); } })); }) : h("p", { className: "ls-empty" }, "No " + filter.toLowerCase() + " notifications.")));
  }

  /* ---------- lab screens ---------- */
  function LabBoard(p) {
    var st = useState(""), q = st[0], setQ = st[1];
    var mine = p.cases.filter(function (c) { return c.lab === ME_LAB; });
    var t = q.trim().toLowerCase();
    var shown = mine.filter(function (c) { return !t || (c.id + c.patient + c.doctor).toLowerCase().indexOf(t) >= 0; });
    return h(React.Fragment, null,
      h(Head, { title: "Board", lead: mine.length + " cases in progress · " + p.incoming.length + " new orders waiting" }),
      h(Panel, { id: "inc", title: "New orders", meta: "Reply within 2 hours" },
        h(L.ActionQueue, { empty: "No new orders right now.", items: p.incoming.map(function (o) {
          return { id: o.id, title: "New order " + o.id + " · " + o.doctor, detail: o.patient + " · " + o.work + " · teeth " + o.teeth + " · shade " + o.shade + " · " + o.pickup, amount: PRICE[o.workType] * o.units,
            actions: [{ label: "Decline", variant: "danger", onClick: function () { p.act("decline", o.id); } }, { label: "Accept", onClick: function () { p.act("accept", o.id); } }] };
        }) })),
      h("div", { className: "pg-boardbar" },
        h("div", { className: "pg-inline" }, h(L.Field, { label: "Filter the board", placeholder: "Order number, doctor or patient", value: q, onChange: function (e) { setQ(e.target.value); } })),
        h("span", { className: "pg-hint" }, shown.length + " of " + mine.length + " shown")),
      h(L.KanbanBoard, { cards: shown.map(function (c) { return { id: c.id, patient: c.patient, doctor: c.doctor, work: c.work, stage: c.stage, note: c.dates && c.dates[c.stage] || "Due " + c.due }; }),
        onMove: function (card) { p.act(card.stage === "glaze" ? "deliver" : "move", card.id); } }),
      h("p", { className: "pg-hint", style: { marginTop: 10 } }, "Open a case from the search above to see its chat and files."));
  }

  function LabInvoices(p) {
    var mo = useState("October 2026"), month = mo[0], setMonth = mo[1];
    var t = INVOICES.reduce(function (a, r) { a.o += r.orders; a.b += r.billed; a.p += r.paid; return a; }, { o: 0, b: 0, p: 0 }), fee = t.b * 0.05;
    return h(React.Fragment, null,
      h(Head, { title: "Invoices", lead: month + " · " + INVOICES.length + " doctors · " + t.o + " orders", actions: [h("div", { key: "m", className: "pg-inline" }, h(L.Field, { label: "Month", as: "select", options: ["October 2026", "September 2026"], value: month, onChange: function (e) { setMonth(e.target.value); } })), h(L.Button, { key: "x", onClick: function () { p.toast("The Excel file, with a dashboard sheet, is generated in the live app."); } }, "Export to Excel")] }),
      h(L.KpiStrip, { items: [{ label: "Your payout", value: egp(t.b - fee), note: "Billed less the 5% fee", lead: true }, { label: "Billed", value: egp(t.b) }, { label: "Still owed by doctors", value: egp(t.b - t.p) }, { label: "Labstage fee, 5%", value: egp(fee) }] }),
      h("div", { style: { height: 20 } }),
      h(Panel, { id: "iv", title: "By doctor" }, h("div", { className: "ls-table-wrap" }, h("table", { className: "ls-table" },
        h("thead", null, h("tr", null, ["Doctor", "Orders", "Billed", "Paid", "Owed", "Fee 5%", "Payout", ""].map(function (x, k) { return h("th", { key: k, className: k ? "num" : undefined }, x); }))),
        h("tbody", null, INVOICES.map(function (r) { var owed = r.billed - r.paid; return h("tr", { key: r.doctor }, h("td", null, h("b", null, r.doctor), h("span", { className: "sub" }, r.clinic)), h("td", { className: "num" }, r.orders), h("td", { className: "num" }, egp(r.billed)), h("td", { className: "num" }, egp(r.paid)), h("td", { className: "num" }, owed ? h(L.Pill, { tone: "warn" }, egp(owed)) : h("span", { className: "sub" }, "Settled")), h("td", { className: "num" }, egp(r.billed * 0.05)), h("td", { className: "num" }, egp(r.billed * 0.95)), h("td", { className: "num" }, h(L.Button, { variant: "ghost", size: "sm", onClick: function () { p.toast("The PDF is generated in the live app."); } }, "PDF"))); })),
        h("tfoot", null, h("tr", null, h("td", null, "Total"), h("td", { className: "num" }, t.o), h("td", { className: "num" }, egp(t.b)), h("td", { className: "num" }, egp(t.p)), h("td", { className: "num" }, egp(t.b - t.p)), h("td", { className: "num" }, egp(fee)), h("td", { className: "num" }, egp(t.b - fee)), h("td")))))));
  }

  function LabAds(p) {
    var a = useState("Giza · Dokki"), area = a[0], setArea = a[1], d = useState("14 days"), dur = d[0], setDur = d[1];
    var lab = LABS[0];
    return h(React.Fragment, null,
      h(Head, { title: "Ads", lead: "Show your lab first when doctors search an area. Ads are always marked Sponsored." }),
      h(Grid, {
        main: [
          h(Panel, { key: "nw", id: "nw", title: "New ad" }, h("div", { className: "pg-pad pg-stack" },
            h("div", { className: "pg-row2" },
              h(L.Field, { label: "Where", as: "select", options: ["Giza · Dokki", "Giza · Mohandessin", "Giza · all areas", "Cairo · Nasr City"], value: area, onChange: function (e) { setArea(e.target.value); } }),
              h(L.Field, { label: "How long", as: "select", options: ["7 days", "14 days", "30 days"], value: dur, onChange: function (e) { setDur(e.target.value); } })),
            h("dl", { className: "pg-dl" }, h("dt", null, "Placement"), h("dd", null, "First in Find a lab, " + area), h("dt", null, "Price"), h("dd", null, "[Ad price, to be set by Labstage]")),
            h(L.Button, { variant: "primary", onClick: function () { p.toast("Ad request sent for " + area + ", " + dur + ". Payment is simulated."); } }, "Start ad"))),
          h(Panel, { key: "cu", id: "cu", title: "Your ads" }, h("div", { className: "ls-table-wrap" }, h("table", { className: "ls-table" },
            h("thead", null, h("tr", null, h("th", null, "Placement"), h("th", null, "Status"), h("th", null, "Ends"), h("th", { className: "num" }, "Profile opens"))),
            h("tbody", null, h("tr", null, h("td", null, "Find a lab · Giza · Dokki"), h("td", null, h(L.Pill, { tone: "success" }, "Running")), h("td", null, "Mon 3 Nov"), h("td", { className: "num" }, "37")),
              h("tr", null, h("td", null, "Find a lab · Giza · all areas"), h("td", null, h(L.Pill, null, "Ended")), h("td", null, "Sun 12 Oct"), h("td", { className: "num" }, "52"))))))
        ],
        rail: h(Panel, { id: "ap", title: "How doctors see it", meta: area }, h(L.LabRow, { lab: lab }))
      }));
  }

  /* ---------- app ---------- */
  function parse() { var m = /^#\/(doctor|lab)\/(.+)$/.exec(location.hash || ""); return m ? { role: m[1], path: m[2] } : { role: "doctor", path: "cases" }; }
  function App() {
    var r = useState(parse), route = r[0], setRoute = r[1];
    var cs = useState(CASES0), cases = cs[0], setCases = cs[1];
    var ic = useState(INCOMING0), incoming = ic[0], setIncoming = ic[1];
    var ns = useState(NOTES0), notes = ns[0], setNotes = ns[1];
    var ol = useState(LABS[0]), orderLab = ol[0], setOrderLab = ol[1];
    var ts = useState(""), toast = ts[0], setToast = ts[1];
    var mainRef = useRef(null);
    var role = route.role;
    function go(path, rl) { setRoute({ role: rl || role, path: path }); window.scrollTo(0, 0); }
    function say(t) { setToast(t); clearTimeout(say.t); say.t = setTimeout(function () { setToast(""); }, 2600); }
    function upd(id, fn) { setCases(cases.map(function (c) { return c.id === id ? fn(Object.assign({}, c)) : c; })); }
    function act(a, id) {
      if (a === "approve") { upd(id, function (c) { c.waiting = "pay"; return c; }); say("Design approved. The first half is now due."); }
      else if (a === "pay") { upd(id, function (c) { c.paidHalves += 1; c.waiting = null; return c; }); say("Payment simulated. Receipt PDF added to " + id + "."); }
      else if (a === "move") { var nx; upd(id, function (c) { nx = c.stages[c.stages.indexOf(c.stage) + 1]; c.stage = nx; c.dates = Object.assign({}, c.dates); c.dates[nx] = "Since just now"; return c; }); say(id + " moved on. The doctor has been told."); }
      else if (a === "deliver") { setCases(cases.filter(function (c) { return c.id !== id; })); say(id + " delivered. The second half was requested from the doctor."); if (route.path.indexOf("case/") === 0) go("board"); }
      else if (a === "accept" || a === "decline") {
        var o = incoming.filter(function (x) { return x.id === id; })[0]; setIncoming(incoming.filter(function (x) { return x.id !== id; }));
        if (a === "accept") { setCases([Object.assign({}, o, { dates: { design: "Accepted just now" } })].concat(cases)); say("Order " + id + " accepted. " + o.doctor + " has been told."); }
        else say("Order " + id + " declined. " + o.doctor + " has been told and nothing was charged.");
      }
    }
    function readNote(n) { var nn = Object.assign({}, notes); nn[role] = notes[role].map(function (x) { return x.id === n.id ? Object.assign({}, x, { unread: false }) : x; }); setNotes(nn); go(n.go); }
    function readAll() { var nn = Object.assign({}, notes); nn[role] = notes[role].map(function (x) { return Object.assign({}, x, { unread: false }); }); setNotes(nn); say("All notifications marked as read."); }
    var unread = notes[role].filter(function (n) { return n.unread; }).length;
    var waitingCount = cases.filter(function (c) { return c.doctor === ME_DOCTOR && c.waiting; }).length;
    var NAV = role === "doctor"
      ? [["cases", "Cases", "cases", waitingCount], ["new", "New order", "plus"], ["labs", "Find a lab", "labs"], ["payments", "Payments", "money"], ["notifications", "Notifications", "bell", unread]]
      : [["board", "Board", "board", incoming.length], ["invoices", "Invoices", "money"], ["profile", "Profile", "profile"], ["ads", "Ads", "megaphone"], ["notifications", "Notifications", "bell", unread]];
    var base = route.path.split("/")[0];
    var current = base === "case" ? (role === "lab" ? "board" : "cases") : base === "lab" ? "labs" : base;
    var P = { role: role, cases: cases, incoming: incoming, notes: notes[role], go: go, act: act, toast: say, readNote: readNote, readAll: readAll, orderLab: orderLab, setOrderLab: setOrderLab };
    var page;
    if (base === "case") page = h(CasePage, Object.assign({ id: route.path.split("/")[1] }, P));
    else if (role === "doctor") page = base === "new" ? h(DoctorNew, P) : base === "labs" ? h(DoctorLabs, P) : base === "lab" ? h(LabProfile, Object.assign({ name: decodeURIComponent(route.path.split("/")[1]) }, P)) : base === "payments" ? h(DoctorPayments, P) : base === "notifications" ? h(Notifications, P) : h(DoctorCases, P);
    else page = base === "invoices" ? h(LabInvoices, P) : base === "profile" ? h(LabProfile, Object.assign({ name: ME_LAB }, P)) : base === "ads" ? h(LabAds, P) : base === "notifications" ? h(Notifications, P) : h(LabBoard, P);
    return h(L.AppShell, {
      role: role, scope: role === "doctor" ? "Dokki Dental Clinic" : "Nile Precision Lab · Dokki", groupLabel: role === "doctor" ? "Clinic" : "Lab", current: current,
      user: role === "doctor" ? { initials: "KA", name: ME_DOCTOR, role: "Doctor" } : { initials: "NP", name: "Hany Farouk", role: "Lab owner" },
      nav: NAV.map(function (n) { return { id: n[0], label: n[1], icon: n[2], count: n[3] || undefined, href: "#/" + role + "/" + n[0], onClick: function (e) { e.preventDefault(); go(n[0]); } }; }),
      footer: h("div", { className: "pg-switch", role: "group", "aria-label": "Demo: switch role" },
        h("button", { type: "button", "aria-pressed": role === "doctor", onClick: function () { go("cases", "doctor"); } }, "Doctor"),
        h("button", { type: "button", "aria-pressed": role === "lab", onClick: function () { go("board", "lab"); } }, "Lab"))
    },
      h("div", { className: "pg-top" }, h(Search, { key: role + route.path, role: role, cases: role === "doctor" ? cases.filter(function (c) { return c.doctor === ME_DOCTOR; }) : cases.filter(function (c) { return c.lab === ME_LAB; }), incoming: incoming, go: go }),
        h(L.Button, { variant: "ghost", icon: "bell", "aria-label": unread + " unread notifications", onClick: function () { go("notifications"); } }, unread ? String(unread) : null)),
      h("div", { ref: mainRef, key: role + route.path }, page),
      h("div", { className: "pg-toast" + (toast ? " on" : ""), role: "status", "aria-live": "polite" }, toast));
  }
  ReactDOM.createRoot(document.getElementById("app")).render(h(App));
})();
