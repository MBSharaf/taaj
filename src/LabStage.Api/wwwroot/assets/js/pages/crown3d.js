// Procedural 3D dental crown whose material morphs through the four production stages.
// Uses Three.js WebGL with physical clearcoat shading and smooth 3D rotation.
import * as THREE from '../../vendor/three/three.module.min.js';

// Per-stage look. Index: 0 Design, 1 Milling, 2 Layering, 3 Glazing.
const LOOKS = [
  { color: '#BFE0FF', rough: 0.06, coat: 1.0, opacity: 1.0, wire: 0.5, ring: 0.0, trans: 1.0 },
  { color: '#f2f1ec', rough: 0.95, coat: 0.0, opacity: 1.0, wire: 0.0, ring: 1.0, trans: 0.0 },
  { color: '#ead9b8', rough: 0.5, coat: 0.25, opacity: 1.0, wire: 0.0, ring: 0.0, trans: 0.0 },
  { color: '#f3e9d4', rough: 0.14, coat: 1.0, opacity: 1.0, wire: 0.0, ring: 0.0, trans: 0.0 },
];

function smoothstep(a, b, x) { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); }

function crownGeometry(radial = 96, profile = 36) {
  // Half-profile from the margin (bottom) to the occlusal fossa (centre top). Wider than tall, like a molar.
  const pts = [
    [0.6, -0.42], [0.67, -0.3], [0.77, -0.1], [0.8, 0.08], [0.78, 0.24],
    [0.7, 0.36], [0.56, 0.44], [0.38, 0.46], [0.2, 0.42], [0.0, 0.38],
  ].map(([x, y]) => new THREE.Vector2(x, y));
  const geo = new THREE.LatheGeometry(new THREE.SplineCurve(pts).getPoints(profile), radial);
  const pos = geo.attributes.position;
  const v = new THREE.Vector3();
  for (let i = 0; i < pos.count; i++) {
    v.fromBufferAttribute(pos, i);
    const theta = Math.atan2(v.z, v.x);
    const r = Math.hypot(v.x, v.z);
    // Molar outline: slightly wider mesio-distally.
    v.x *= 1.1; v.z *= 0.93;
    // Four cusps rising on the occlusal table, with a central fissure.
    const top = smoothstep(0.15, 0.46, v.y);
    const cusp = Math.pow(0.5 + 0.5 * Math.cos(4 * theta + Math.PI / 4), 1.6);
    const ridge = smoothstep(0.05, 0.55, r) * (1 - smoothstep(0.62, 0.8, r));
    v.y += top * ridge * cusp * 0.14 - top * (1 - smoothstep(0.0, 0.25, r)) * 0.04;
    // Gentle organic irregularity.
    v.y += top * 0.015 * Math.sin(theta * 7 + r * 9);
    pos.setXYZ(i, v.x, v.y, v.z);
  }
  geo.computeVertexNormals();
  geo.center();
  return geo;
}

function studioEnvironment(renderer) {
  // Small procedural studio so clearcoat/gloss reads like glazed ceramic.
  const scene = new THREE.Scene();
  const room = new THREE.Mesh(new THREE.BoxGeometry(10, 10, 10), new THREE.MeshBasicMaterial({ color: 0x0B1A45, side: THREE.BackSide }));
  scene.add(room);
  const panel = (w, h, c, p, look) => {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshBasicMaterial({ color: c, side: THREE.DoubleSide }));
    m.position.set(...p); m.lookAt(...look); scene.add(m);
  };
  panel(6, 2, 0xffffff, [0, 4.5, 0], [0, 0, 0]);
  panel(2, 4, 0xCFE2FF, [4.5, 1, 2], [0, 0, 0]);
  panel(2, 3, 0x4DA8FF, [-4.5, 0, -2], [0, 0, 0]);
  const pmrem = new THREE.PMREMGenerator(renderer);
  const tex = pmrem.fromScene(scene, 0.04).texture;
  pmrem.dispose();
  return tex;
}

/**
 * Mounts the real 3D WebGL crown into `host`. Returns a controller, or throws if WebGL is unavailable.
 * @param {HTMLElement} host
 * @param {{ reducedMotion?: boolean }} [opts]
 */
export function mountCrown(host, opts = {}) {
  const reduced = !!opts.reducedMotion;
  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.75));
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.05;
  renderer.domElement.setAttribute('aria-hidden', 'true');
  host.appendChild(renderer.domElement);

  const fallback = host.querySelector('.crown-fallback, .dash-3d-wire-fallback');
  if (fallback) fallback.style.display = 'none';

  const scene = new THREE.Scene();
  scene.environment = studioEnvironment(renderer);
  const camera = new THREE.PerspectiveCamera(32, 1, 0.1, 50);
  camera.position.set(0, 2.1, 4.2);
  camera.lookAt(0, -0.1, 0);

  scene.add(new THREE.HemisphereLight(0xffffff, 0x0B1A45, 0.6));
  const key = new THREE.DirectionalLight(0xffffff, 2.2); key.position.set(3, 4, 3); scene.add(key);
  const rim = new THREE.DirectionalLight(0x7FE3FF, 1.8); rim.position.set(-3, 1, -3); scene.add(rim);

  const geo = crownGeometry();
  const mat = new THREE.MeshPhysicalMaterial({ color: LOOKS[0].color, roughness: 0.06, transparent: true, side: THREE.DoubleSide, clearcoatRoughness: 0.06, thickness: 0.9, ior: 1.45, iridescence: 0.35, iridescenceIOR: 1.3 });
  const crown = new THREE.Mesh(geo, mat);
  const wireGeo = crownGeometry(40, 14);
  const wireMat = new THREE.MeshBasicMaterial({ color: 0x7FE3FF, wireframe: true, transparent: true, opacity: 1 });
  const wire = new THREE.Mesh(wireGeo, wireMat);
  wire.scale.setScalar(1.004);

  // Milling: orbiting tool-path ring around the zirconia blank.
  const ringMat = new THREE.MeshBasicMaterial({ color: 0x4DA8FF, transparent: true, opacity: 0 });
  const ring = new THREE.Mesh(new THREE.TorusGeometry(1.25, 0.008, 8, 160), ringMat);
  ring.rotation.x = Math.PI / 2.3;

  const group = new THREE.Group();
  group.add(crown, wire, ring);
  scene.add(group);

  const cA = new THREE.Color(); const cB = new THREE.Color();
  let target = 0; let current = 0;
  let pointerX = 0; let pointerY = 0;
  let visible = true; let raf = 0; let last = performance.now();

  function applyLook(s) {
    s = Number.isFinite(s) ? Math.max(0, Math.min(3, s)) : 0;
    const i = Math.min(3, Math.floor(s)); const j = Math.min(3, i + 1); const t = s - i;
    const a = LOOKS[i]; const b = LOOKS[j];
    const mix = (k) => a[k] + (b[k] - a[k]) * t;
    mat.color.copy(cA.set(a.color)).lerp(cB.set(b.color), t);
    mat.roughness = mix('rough');
    mat.clearcoat = mix('coat');
    mat.opacity = mix('opacity');
    mat.transmission = mix('trans');
    mat.iridescence = 0.35 * mix('trans');
    mat.depthWrite = mat.opacity > 0.9;
    wireMat.opacity = mix('wire');
    wire.visible = wireMat.opacity > 0.01;
    ringMat.opacity = mix('ring') * 0.9;
    ring.visible = ringMat.opacity > 0.01;
  }

  function resize() {
    const { clientWidth: w, clientHeight: h } = host;
    if (!w || !h) return;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    if (reduced) render(0);
  }

  function render(dt) {
    current = reduced ? target : current + (target - current) * Math.min(1, dt * 4);
    applyLook(current);
    if (!reduced) {
      group.rotation.y += dt * 0.25;
      group.rotation.x += ((pointerY * 0.25) - group.rotation.x) * Math.min(1, dt * 3);
      group.rotation.z += ((-pointerX * 0.15) - group.rotation.z) * Math.min(1, dt * 3);
      ring.rotation.z += dt * 1.4;
    } else {
      group.rotation.set(0.15, 0.6 + current * 0.9, 0);
    }
    renderer.render(scene, camera);
  }

  function loop(t) {
    raf = 0;
    if (!visible || document.hidden) return;
    const dt = Math.max(0, Math.min(0.05, (t - last) / 1000)); last = t;
    render(dt);
    raf = requestAnimationFrame(loop);
  }
  const start = () => { if (!reduced && !raf) { last = performance.now(); raf = requestAnimationFrame(loop); } };

  const ro = new ResizeObserver(resize); ro.observe(host);
  const io = new IntersectionObserver(([e]) => { visible = e.isIntersecting; if (visible) start(); });
  io.observe(host);
  const onVis = () => { if (!document.hidden) start(); };
  document.addEventListener('visibilitychange', onVis);
  const onPointer = (e) => { pointerX = (e.clientX / window.innerWidth) * 2 - 1; pointerY = (e.clientY / window.innerHeight) * 2 - 1; };
  if (!reduced && window.matchMedia('(pointer: fine)').matches) window.addEventListener('pointermove', onPointer, { passive: true });

  resize();
  applyLook(0);
  if (reduced) render(0); else start();

  return {
    /** @param {number} stage float 0..3 */
    setStage(stage) { target = Number.isFinite(stage) ? Math.max(0, Math.min(3, stage)) : 0; if (reduced) render(0); },
    dispose() {
      cancelAnimationFrame(raf); ro.disconnect(); io.disconnect();
      document.removeEventListener('visibilitychange', onVis);
      window.removeEventListener('pointermove', onPointer);
      geo.dispose(); wireGeo.dispose(); mat.dispose(); wireMat.dispose(); ringMat.dispose(); renderer.dispose();
      renderer.domElement.remove();
    },
  };
}
