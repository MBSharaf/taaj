# StageTracker

Shows where a case is in the lab workflow: Design → Milling → Layering → Glazing (التصميم ← الخراطة ← الطبقات ← التلميع). Done stages in `success`, the current one in `primary` with a tint halo, upcoming ones neutral. Follows reading direction (RTL in Arabic). Markup: `ol.tj-stages > li.tj-stage(.done|.now) > span.tj-dot + label`.
