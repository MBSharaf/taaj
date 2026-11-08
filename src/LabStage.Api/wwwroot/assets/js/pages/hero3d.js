// Homepage hero: real-time 3D restorations (procedural three.js models, no external assets).
// The pieces sit in the same "slots" as the SVG showcase: one large piece in the middle, the next one small in the
// bottom-end corner. showcase.js owns the cycle; it calls go(index). All visible text stays in index.html.
import * as THREE from '../../vendor/three/three.module.min.js';

const smooth = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
const lerp = (a, b, t) => a + (b - a) * t;

// cubic-bezier(.7, 0, .2, 1): the same curve the CSS showcase uses.
function bezier(x1, y1, x2, y2) {
  const cx = 3 * x1; const bx = 3 * (x2 - x1) - cx; const ax = 1 - cx - bx;
  const cy = 3 * y1; const by = 3 * (y2 - y1) - cy; const ay = 1 - cy - by;
  const sx = (t) => ((ax * t + bx) * t + cx) * t;
  return (x) => {
    let t = x;
    for (let i = 0; i < 8; i++) { const d = (3 * ax * t + 2 * bx) * t + cx; if (Math.abs(d) < 1e-6) break; t -= (sx(t) - x) / d; }
    t = Math.min(1, Math.max(0, t));
    return ((ay * t + by) * t + cy) * t;
  };
}
const ease = bezier(0.7, 0, 0.2, 1);

/* ---------------------------------------------------------------- geometry */

function paint(geo, stops) {
  // Vertex colours along Y (cervical -> incisal/occlusal), stops: [[t, '#hex'], ...] with t in 0..1.
  geo.computeBoundingBox();
  const { min, max } = geo.boundingBox;
  const pos = geo.attributes.position;
  const cols = new Float32Array(pos.count * 3);
  const cs = stops.map(([t, c]) => [t, new THREE.Color(c)]);
  const c = new THREE.Color();
  for (let i = 0; i < pos.count; i++) {
    const t = (pos.getY(i) - min.y) / (max.y - min.y || 1);
    let k = 0; while (k < cs.length - 2 && t > cs[k + 1][0]) k++;
    const [t0, c0] = cs[k]; const [t1, c1] = cs[k + 1];
    c.copy(c0).lerp(c1, smooth(t0, t1, t));
    cols.set([c.r, c.g, c.b], i * 3);
  }
  geo.setAttribute('color', new THREE.BufferAttribute(cols, 3));
  return geo;
}

/** Molar crown: lathe body, four cusps, central fissure, closed underside. */
function molarGeometry(radial = 120, profile = 48) {
  const pts = [
    [0.0, -0.36], [0.42, -0.42], [0.6, -0.42], [0.67, -0.3], [0.77, -0.1], [0.81, 0.08], [0.79, 0.24],
    [0.71, 0.36], [0.57, 0.44], [0.38, 0.47], [0.2, 0.43], [0.0, 0.39],
  ].map(([x, y]) => new THREE.Vector2(x, y));
  const geo = new THREE.LatheGeometry(new THREE.SplineCurve(pts).getPoints(profile), radial);
  const pos = geo.attributes.position;
  const v = new THREE.Vector3();
  for (let i = 0; i < pos.count; i++) {
    v.fromBufferAttribute(pos, i);
    const theta = Math.atan2(v.z, v.x);
    const r = Math.hypot(v.x, v.z);
    v.x *= 1.1; v.z *= 0.94;
    const top = smooth(0.12, 0.47, v.y);
    const cusp = Math.pow(0.5 + 0.5 * Math.cos(4 * theta + Math.PI / 4), 1.5);
    const ridge = smooth(0.05, 0.55, r) * (1 - smooth(0.64, 0.82, r));
    v.y += top * ridge * cusp * 0.17 - top * (1 - smooth(0.0, 0.26, r)) * 0.06;
    // fissures: a shallow cross groove between the cusps
    const groove = Math.pow(Math.abs(Math.sin(2 * theta + Math.PI / 4)), 8);
    v.y -= top * groove * smooth(0.08, 0.3, r) * (1 - smooth(0.45, 0.62, r)) * 0.035;
    v.y += top * 0.012 * Math.sin(theta * 7 + r * 9);
    // slight bulge on the buccal/lingual walls (height of contour)
    const wall = smooth(-0.4, 0.05, v.y) * (1 - smooth(0.1, 0.4, v.y));
    v.x *= 1 + wall * 0.03; v.z *= 1 + wall * 0.05;
    pos.setXYZ(i, v.x, v.y, v.z);
  }
  geo.computeVertexNormals();
  geo.center();
  return geo;
}

/** Anterior tooth crown (or a thin veneer shell when `shell` is set): a deformed sphere. */
function incisorGeometry({ shell = false } = {}) {
  const geo = new THREE.SphereGeometry(1, 96, 96);
  const pos = geo.attributes.position;
  const v = new THREE.Vector3();
  for (let i = 0; i < pos.count; i++) {
    v.fromBufferAttribute(pos, i);
    // push the sphere toward a rounded box so the crown has a flat labial face and a straight incisal edge
    const sq = (a, k) => Math.sign(a) * Math.abs(a) ** k;
    v.x = sq(v.x, 0.7); v.y = sq(v.y, 0.8);
    const t = (v.y + 1) / 2; // 0 cervical, 1 incisal
    const width = 0.36 + 0.5 * smooth(0.0, 0.7, t) - 0.04 * smooth(0.9, 1, t);
    let thick = shell ? 0.07 + 0.03 * (1 - t) : 0.36 * (1 - 0.78 * smooth(0.3, 1, t)) + 0.05;
    if (!shell && v.z < 0) thick *= 1 + 0.35 * (1 - smooth(0.0, 0.5, t)); // cingulum
    let y = v.y * 1.08;
    if (y > 0.82) y = 0.82 + (y - 0.82) * 0.25; // flat incisal edge
    const x = v.x * width;
    let z = v.z * thick;
    z -= 0.16 * y * y;                        // gentle labial bow
    z += 0.1 * (1 - (x / 0.8) ** 2);         // mesio-distal convexity
    if (shell && v.z < 0) z += 0.03;          // hollow back of the veneer
    // incisal mamelon hint
    const mam = smooth(0.86, 1, t) * 0.012 * Math.cos(x * 9);
    pos.setXYZ(i, x, y + mam, z);
  }
  geo.computeVertexNormals();
  geo.center();
  return geo;
}

function helixThread(len, r0, r1, turns, tube) {
  class Helix extends THREE.Curve {
    getPoint(t, target = new THREE.Vector3()) {
      const a = t * turns * Math.PI * 2;
      const r = lerp(r0, r1, t);
      return target.set(Math.cos(a) * r, -t * len, Math.sin(a) * r);
    }
  }
  return new THREE.TubeGeometry(new Helix(), Math.round(turns * 64), tube, 10, false);
}

/* ---------------------------------------------------------------- materials */

function materials() {
  const zirconia = new THREE.MeshPhysicalMaterial({
    vertexColors: true, roughness: 0.34, clearcoat: 0.85, clearcoatRoughness: 0.14,
    sheen: 0.25, sheenColor: new THREE.Color('#FFF1DA'), sheenRoughness: 0.45, specularIntensity: 0.9,
  });
  const emax = new THREE.MeshPhysicalMaterial({
    vertexColors: true, roughness: 0.16, clearcoat: 1, clearcoatRoughness: 0.04,
    sheen: 0.35, sheenColor: new THREE.Color('#D8ECFF'), sheenRoughness: 0.35,
    iridescence: 0.12, iridescenceIOR: 1.35, specularIntensity: 1,
  });
  const titanium = new THREE.MeshPhysicalMaterial({ color: '#B9C1CC', metalness: 1, roughness: 0.3, clearcoat: 0.25, clearcoatRoughness: 0.3 });
  const polished = new THREE.MeshPhysicalMaterial({ color: '#E3E7EC', metalness: 1, roughness: 0.12 });
  const gold = new THREE.MeshPhysicalMaterial({ color: '#D9B76A', metalness: 1, roughness: 0.22, clearcoat: 0.4, clearcoatRoughness: 0.15 });
  return { zirconia, emax, titanium, polished, gold };
}

const ZR_SHADE = [[0, '#D3B583'], [0.35, '#E8D5B1'], [0.7, '#F3E8D4'], [1, '#FBF7EE']];
const EMAX_SHADE = [[0, '#DCC08F'], [0.4, '#EEE0C6'], [0.72, '#F3EEE6'], [0.9, '#D6E4EF'], [1, '#A9C4DC']];

/* ---------------------------------------------------------------- models */

function buildModels(M) {
  const molar = paint(molarGeometry(), ZR_SHADE);
  const molarSmall = paint(molarGeometry(72, 30), ZR_SHADE);
  const incisor = paint(incisorGeometry(), EMAX_SHADE);
  const veneer = paint(incisorGeometry({ shell: true }), EMAX_SHADE);

  // 0. Multilayer zirconia crown
  const crown = new THREE.Group();
  crown.add(new THREE.Mesh(molar, M.zirconia));

  // 1. e.max anterior crown
  const tooth = new THREE.Group();
  tooth.add(new THREE.Mesh(incisor, M.emax));

  // 2. Three-unit zirconia bridge
  const bridge = new THREE.Group();
  [-1.02, 0, 1.02].forEach((x, i) => {
    const m = new THREE.Mesh(molarSmall, M.zirconia);
    m.position.set(x, i === 1 ? 0.04 : 0, i === 1 ? 0.06 : 0);
    m.rotation.y = (i - 1) * -0.18;
    m.scale.setScalar(i === 1 ? 0.92 : 1);
    bridge.add(m);
  });
  const conn = new THREE.CapsuleGeometry(0.2, 0.3, 8, 24);
  paint(conn, [[0, '#F1E4CB'], [1, '#F1E4CB']]);
  [-0.51, 0.51].forEach((x) => {
    const c = new THREE.Mesh(conn, M.zirconia);
    c.rotation.z = Math.PI / 2; c.position.set(x, -0.02, 0.03); c.scale.set(1, 1, 0.85);
    bridge.add(c);
  });
  bridge.rotation.x = 0; // tilt handled by the slot rig

  // 3. Four e.max veneers on an arch
  const veneers = new THREE.Group();
  [[-1.32, -0.36, 0.55, 0.8, 0.08], [-0.46, 0, 0.14, 1, 0], [0.46, 0, -0.14, 1, 0], [1.32, -0.36, -0.55, 0.8, 0.08]]
    .forEach(([x, z, ry, s, y]) => {
      const m = new THREE.Mesh(veneer, M.emax);
      m.position.set(x, y, z); m.rotation.y = ry; m.scale.setScalar(s);
      veneers.add(m);
    });

  // 4. Implant: titanium fixture + gold (TiN) custom abutment + zirconia crown hovering above
  const implant = new THREE.Group();
  const fixture = new THREE.Group();
  const coreProfile = [[0.0, -1.62], [0.12, -1.6], [0.2, -1.52], [0.24, -1.3], [0.29, -0.6], [0.31, 0], [0.33, 0.02], [0.0, 0.02]]
    .map(([x, y]) => new THREE.Vector2(x, y));
  fixture.add(new THREE.Mesh(new THREE.LatheGeometry(coreProfile, 64), M.titanium));
  const thread = new THREE.Mesh(helixThread(1.38, 0.34, 0.27, 8.5, 0.045), M.titanium);
  thread.position.y = -0.1;
  fixture.add(thread);
  const collar = new THREE.Mesh(new THREE.CylinderGeometry(0.335, 0.32, 0.14, 64), M.polished);
  collar.position.y = 0.05;
  fixture.add(collar);
  implant.add(fixture);
  const abutProfile = [[0.0, 0.12], [0.3, 0.12], [0.37, 0.24], [0.4, 0.36], [0.36, 0.44], [0.3, 0.5], [0.24, 0.98], [0.2, 1.04], [0.0, 1.05]]
    .map(([x, y]) => new THREE.Vector2(x, y));
  const abut = new THREE.Mesh(new THREE.LatheGeometry(abutProfile, 64), M.gold);
  abut.scale.set(1.08, 1, 0.92);
  implant.add(abut);
  const cap = new THREE.Mesh(molar, M.zirconia);
  cap.scale.setScalar(0.92);
  cap.position.y = 1.62;
  cap.name = 'hover';
  implant.add(cap);

  return [
    { obj: crown, tilt: 0.34, mode: 'full', shadowW: 0.85 },
    { obj: tooth, tilt: 0.06, mode: 'sway', sway: 0.55, shadowW: 0.6 },
    { obj: bridge, tilt: 0.42, mode: 'sway', sway: 0.5, shadowW: 1.05 },
    { obj: veneers, tilt: 0.12, mode: 'sway', sway: 0.35, shadowW: 1.0 },
    { obj: implant, tilt: 0.06, mode: 'full', shadowW: 0.45 },
  ];
}

function normalise(obj) {
  // Centre the piece and scale its largest extent to 2 units.
  const box = new THREE.Box3().setFromObject(obj);
  const size = box.getSize(new THREE.Vector3());
  const centre = box.getCenter(new THREE.Vector3());
  const s = 2 / Math.max(size.x, size.y, size.z);
  const wrap = new THREE.Group();
  obj.position.sub(centre);
  wrap.add(obj);
  wrap.scale.setScalar(s);
  return { wrap, height: size.y * s };
}

/* ---------------------------------------------------------------- studio */

function studio(renderer) {
  // A small "room" lit by soft-box panels, pre-filtered into an environment map (RoomEnvironment-style).
  const scene = new THREE.Scene();
  const room = new THREE.Mesh(new THREE.BoxGeometry(12, 8, 12), new THREE.MeshStandardMaterial({ color: '#5B6475', side: THREE.BackSide }));
  room.position.y = 2;
  scene.add(room);
  scene.add(new THREE.AmbientLight(0xffffff, 1.6));
  const box = (w, h, x, y, z, intensity, tint = '#ffffff') => {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshBasicMaterial({ side: THREE.DoubleSide }));
    m.material.color.set(tint).multiplyScalar(intensity);
    m.position.set(x, y, z); m.lookAt(0, 0, 0);
    scene.add(m);
  };
  box(6, 3, 0, 5.8, 1, 9);              // overhead soft box
  box(2.4, 5, -5.5, 1.5, 2, 6);         // key strip, left
  box(2.4, 5, 5.5, 1.2, -1, 4, '#DCEBFF'); // cool fill, right
  box(5, 2, 0, 1, -5.8, 3, '#BFD9FF');  // back rim
  box(4, 1.2, 0, -2.2, 5.5, 2.2);       // front bounce card
  const pmrem = new THREE.PMREMGenerator(renderer);
  const env = pmrem.fromScene(scene, 0.035).texture;
  pmrem.dispose();
  scene.traverse((o) => { o.geometry?.dispose(); o.material?.dispose(); });
  return env;
}

function shadowTexture() {
  const c = document.createElement('canvas'); c.width = c.height = 128;
  const g = c.getContext('2d');
  const grd = g.createRadialGradient(64, 64, 0, 64, 64, 64);
  grd.addColorStop(0, 'rgba(0,0,0,0.55)'); grd.addColorStop(0.45, 'rgba(0,0,0,0.26)'); grd.addColorStop(1, 'rgba(0,0,0,0)');
  g.fillStyle = grd; g.fillRect(0, 0, 128, 128);
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

/* ---------------------------------------------------------------- mount */

/**
 * @param {HTMLElement} frame  the .sc-frame (canvas covers it)
 * @param {{ host: HTMLElement, stage: HTMLElement, grab?: HTMLElement, count: number, index?: number,
 *           reducedMotion?: boolean, onHold?: (holding: boolean) => void }} opts
 */
export function mountHero3D(frame, opts) {
  const { host, stage, grab } = opts;
  const reduced = !!opts.reducedMotion;
  // Probe first so a missing WebGL fails quietly (three.js would log an error) and the SVG fallback stays.
  const probe = document.createElement('canvas');
  if (!(probe.getContext('webgl2') || probe.getContext('webgl'))) throw new Error('WebGL unavailable');
  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 0.92;
  renderer.setClearColor(0x000000, 0);
  host.prepend(renderer.domElement);

  const scene = new THREE.Scene();
  scene.environment = studio(renderer);
  scene.environmentIntensity = 0.75;
  const FOV = 26; const DIST = 12;
  const camera = new THREE.PerspectiveCamera(FOV, 1, 0.1, 60);
  camera.position.set(0, 0, DIST);
  const halfH = DIST * Math.tan(THREE.MathUtils.degToRad(FOV / 2));

  scene.add(new THREE.HemisphereLight(0xffffff, 0x1a2340, 0.55));
  const key = new THREE.DirectionalLight(0xffffff, 1.9); key.position.set(-4, 6, 6); scene.add(key);
  const rim = new THREE.DirectionalLight(0xBFE6FF, 2.2); rim.position.set(5, 2, -6); scene.add(rim);

  const M = materials();
  const shadowTex = shadowTexture();
  const shadowGeo = new THREE.PlaneGeometry(1, 1);

  const pieces = buildModels(M).map((p) => {
    const { wrap, height } = normalise(p.obj);
    const rig = new THREE.Group();     // slot position + scale
    const tilt = new THREE.Group();    // lean toward the camera
    const spin = new THREE.Group();    // auto-rotation + drag
    tilt.rotation.x = p.tilt;
    const tiltX = p.tilt;
    spin.add(wrap); tilt.add(spin); rig.add(tilt);
    const shadow = new THREE.Mesh(shadowGeo, new THREE.MeshBasicMaterial({ map: shadowTex, transparent: true, depthWrite: false, opacity: 0.9 }));
    shadow.renderOrder = -1;
    scene.add(rig, shadow);
    return { ...p, tiltX, rig, tilt, spin, shadow, height, hover: wrap.getObjectByName('hover'),
      from: null, to: null, t: 1, yaw: Math.random() * Math.PI * 2, twist: 0, phase: Math.random() * 6 };
  });

  const n = pieces.length;
  let index = opts.index ?? 0;
  let W = 1; let H = 1;
  let slots = null;
  let visible = true; let raf = 0; let last = performance.now(); let time = 0;
  let dirty = true;

  // Drag-to-rotate state (applies to the active piece)
  let dragging = false; let dragX = 0; let dragY = 0; let dragVel = 0; let dragYaw = 0; let dragPitch = 0;

  const endSign = () => (document.documentElement.dir === 'rtl' ? -1 : 1);

  function computeSlots() {
    // layout boxes (offset*), not getBoundingClientRect: the stage may still be mid entrance-animation (scaled)
    W = host.clientWidth; H = host.clientHeight;
    const sr = { left: stage.offsetLeft, top: stage.offsetTop, width: stage.offsetWidth, height: stage.offsetHeight };
    const fr = { left: host.offsetLeft, top: host.offsetTop };
    const end = endSign();
    const mobile = W <= 900;
    const sx = sr.left - fr.left + sr.width / 2; const sy = sr.top - fr.top + sr.height / 2;
    const cw = sr.width; const ch = sr.height;
    const size = mobile ? Math.min(cw * 0.78, ch * 0.9) : Math.min(cw * 0.34, ch * 0.66);
    const cx = mobile ? sx : sx + end * cw * 0.1;
    const cy = mobile ? sy + ch * 0.02 : sy + ch * 0.03;
    // px -> world on the z=0 plane
    const k = (2 * halfH) / H;
    const P = (x, y, s) => ({ x: (x - W / 2) * k, y: (H / 2 - y) * k, s: (s * k) / 2 });
    const nx = mobile ? 0.38 : 0.37; const ny = mobile ? 0.3 : 0.31;
    return {
      size,
      active: P(cx, cy, size),
      next: P(sx + end * cw * nx, sy + ch * ny, size * (mobile ? 0.22 : 0.27)),
      after: P(sx + end * cw * (nx + 0.085), sy + ch * (ny + 0.05), size * 0.15),
      prev: P(cx - end * cw * 0.12, -size * 1.3, size * 0.55),
      parked: P(sx + end * (cw * 0.5 + size * 0.4), sy + ch * 0.45, size * 0.08),
    };
  }

  const slotName = (d) => (d === 0 ? 'active' : d === 1 ? 'next' : d === 2 ? 'after' : d === n - 1 ? 'prev' : 'parked');

  function place(instant) {
    pieces.forEach((p, k) => {
      const d = (k - index + n) % n;
      const name = slotName(d);
      const target = slots[name];
      const snap = instant || reduced || (p.slot === 'prev' && name === 'parked') || (p.slot === 'parked' && name === 'prev');
      const cur = p.to ? sample(p) : target;
      if (name === 'active' && p.slot !== 'active' && !instant && !reduced) p.twist = Math.PI * 1.15; // spin in
      p.slot = name;
      p.from = snap ? target : cur;
      p.to = target;
      p.t = snap ? 1 : 0;
      p.t0 = performance.now();
    });
    dirty = true;
  }

  function sample(p) {
    const e = ease(Math.min(1, p.t));
    return { x: lerp(p.from.x, p.to.x, e), y: lerp(p.from.y, p.to.y, e), s: lerp(p.from.s, p.to.s, e) };
  }

  function resize() {
    const w = host.clientWidth; const h = host.clientHeight;
    if (!w || !h) return;
    renderer.setSize(w, h, false);
    camera.aspect = w / h; camera.updateProjectionMatrix();
    slots = computeSlots();
    place(true);
    if (grab) {
      const a = slots.active; const k = H / (2 * halfH);
      const px = a.x * k + W / 2; const py = H / 2 - a.y * k; const r = slots.size * 0.42;
      Object.assign(grab.style, { left: `${px - r}px`, top: `${py - r}px`, width: `${2 * r}px`, height: `${2 * r}px` });
    }
    render(0);
  }

  function render(dt) {
    time += dt;
    const MOVE = 1.15;
    pieces.forEach((p, k) => {
      if (p.t < 1) p.t = Math.min(1, (performance.now() - p.t0) / 1000 / MOVE);
      const s = sample(p);
      const active = p.slot === 'active';
      const showing = p.slot !== 'parked' || p.t < 1;
      p.rig.visible = showing; p.shadow.visible = showing && p.slot !== 'prev';
      if (!showing) return;
      const bob = reduced ? 0 : Math.sin(time * 1.15 + p.phase) * 0.045;
      p.rig.position.set(s.x, s.y + bob * s.s, active ? 0.4 : 0);
      p.rig.scale.setScalar(s.s);
      // rotation: slow turn (or sway), spin-in on arrival, drag on the active piece
      if (!reduced) {
        p.yaw += dt * (active ? 0.42 : 0.25);
        p.twist *= Math.pow(0.08, dt);  // decays over ~1s
      }
      let yaw = p.mode === 'full' ? p.yaw : Math.sin(p.yaw * 0.9) * (p.sway ?? 0.6);
      yaw += p.twist;
      if (reduced) yaw = p.mode === 'full' ? 0.6 : 0.2;
      if (active) yaw += dragYaw;
      p.spin.rotation.y = yaw;
      p.tilt.rotation.x = p.tiltX + (active ? dragPitch : 0);
      p.tilt.rotation.z = reduced ? 0 : Math.sin(time * 0.7 + p.phase) * 0.05;
      if (p.hover) p.hover.position.y = 1.62 + (reduced ? 0.1 : 0.1 + Math.sin(time * 1.6) * 0.07);
      // contact shadow: flattened ellipse under the piece, shrinks as it rises
      const lift = 1 - bob * 4;
      p.shadow.position.set(s.x, s.y - s.s * (p.height / 2 + 0.12), -0.6);
      p.shadow.scale.set(s.s * 1.9 * p.shadowW * lift, s.s * 0.32 * lift, 1);
      p.shadow.material.opacity = (active ? 0.85 : 0.6) * Math.min(1, s.s * 3);
    });
    if (!dragging) { dragYaw += dragVel; dragVel *= 0.92; dragPitch *= 0.9; }
    renderer.render(scene, camera);
    dirty = false;
  }

  function loop(t) {
    raf = 0;
    if (!visible || document.hidden) return;
    const dt = Math.max(0, Math.min(0.05, (t - last) / 1000)); last = t;
    render(dt);
    if (!reduced || pieces.some((p) => p.t < 1) || dragging || Math.abs(dragVel) > 1e-4) raf = requestAnimationFrame(loop);
  }
  const start = () => { if (!raf && visible && !document.hidden) { last = performance.now(); raf = requestAnimationFrame(loop); } };

  const ro = new ResizeObserver(() => { resize(); start(); });
  ro.observe(host);
  const io = new IntersectionObserver(([e]) => { visible = e.isIntersecting; if (visible) start(); }, { threshold: 0 });
  io.observe(frame);
  const onVis = () => { if (!document.hidden) start(); };
  document.addEventListener('visibilitychange', onVis);
  // language switch flips dir: recompute the slots
  const mo = new MutationObserver(() => { resize(); start(); });
  mo.observe(document.documentElement, { attributes: true, attributeFilter: ['dir'] });

  if (grab) {
    grab.addEventListener('pointerdown', (e) => {
      dragging = true; dragX = e.clientX; dragY = e.clientY; dragVel = 0;
      grab.setPointerCapture(e.pointerId); grab.classList.add('is-dragging');
      opts.onHold?.(true); start();
    });
    grab.addEventListener('pointermove', (e) => {
      if (!dragging) return;
      const dx = e.clientX - dragX; const dy = e.clientY - dragY;
      dragX = e.clientX; dragY = e.clientY;
      dragVel = dx * 0.008; dragYaw += dragVel;
      dragPitch = Math.max(-0.5, Math.min(0.5, dragPitch + dy * 0.004));
    });
    const end = () => { if (!dragging) return; dragging = false; grab.classList.remove('is-dragging'); opts.onHold?.(false); start(); };
    grab.addEventListener('pointerup', end);
    grab.addEventListener('pointercancel', end);
  }

  resize();
  start();

  return {
    go(i) { index = ((i % n) + n) % n; dragYaw = 0; dragPitch = 0; dragVel = 0; place(false); start(); },
    dispose() {
      cancelAnimationFrame(raf); ro.disconnect(); io.disconnect(); mo.disconnect();
      document.removeEventListener('visibilitychange', onVis);
      renderer.dispose(); renderer.domElement.remove();
    },
  };
}
