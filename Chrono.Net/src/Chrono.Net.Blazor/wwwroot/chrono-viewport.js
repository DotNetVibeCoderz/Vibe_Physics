// Chrono.Net WebGL viewport (three.js). Made by Gravicode Studios, led by Kang Fadhil.
import * as THREE from './lib/three.module.min.js';
import { OrbitControls } from './lib/OrbitControls.js';

const views = new Map();
let nextId = 1;

function geometryFor(item) {
  const d = item.dims;
  let g;
  switch (item.shape) {
    case 'box': g = new THREE.BoxGeometry(d[0], d[1], d[2]); break;
    case 'sphere': g = new THREE.SphereGeometry(d[0], 32, 20); break;
    case 'ellipsoid': g = new THREE.SphereGeometry(1, 32, 20); g.scale(d[0], d[1], d[2]); break;
    // three.js primitives are Y-aligned; Chrono's are Z-aligned and centered.
    case 'cylinder': g = new THREE.CylinderGeometry(d[0], d[0], d[1], 36); g.rotateX(Math.PI / 2); break;
    case 'cone': g = new THREE.ConeGeometry(d[0], d[1], 36); g.rotateX(Math.PI / 2); break;
    case 'capsule': g = new THREE.CapsuleGeometry(d[0], d[1], 8, 24); g.rotateX(Math.PI / 2); break;
    case 'mesh': {
      g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(item.vertices, 3));
      g.setIndex(item.indices);
      g.computeVertexNormals();
      break;
    }
    default: g = new THREE.BoxGeometry(0.1, 0.1, 0.1);
  }
  return g;
}

export function create(canvas, options) {
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.outputColorSpace = THREE.SRGBColorSpace;

  const scene = new THREE.Scene();
  const bg = new THREE.Color(options?.background ?? '#12161F');
  scene.background = bg;
  scene.fog = new THREE.Fog(bg, 30, 140);

  const camera = new THREE.PerspectiveCamera(45, 1, 0.05, 2000);
  const controls = new OrbitControls(camera, canvas);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;

  const hemi = new THREE.HemisphereLight('#c9d6ea', '#1d2433', 1.1);
  scene.add(hemi);
  const sun = new THREE.DirectionalLight('#fff4e3', 2.2);
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.bias = -0.0005;
  scene.add(sun, sun.target);

  const view = {
    renderer, scene, camera, controls, sun, hemi, canvas,
    root: new THREE.Group(), bodies: [], statics: [], grid: null, lines: null,
    follow: -1, upIsZ: false, disposed: false,
  };
  scene.add(view.root);

  const resize = () => {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    if (w === 0 || h === 0) return;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
  };
  view.resizeObserver = new ResizeObserver(resize);
  view.resizeObserver.observe(canvas);
  resize();

  const loop = () => {
    if (view.disposed) return;
    controls.update();
    renderer.render(scene, camera);
    view.raf = requestAnimationFrame(loop);
  };
  loop();

  const id = nextId++;
  views.set(id, view);
  return id;
}

export function setScene(id, desc, camera) {
  const v = views.get(id);
  if (!v) return;
  v.root.clear();
  v.bodies = [];
  v.upIsZ = desc.upIsZ;
  const up = desc.upIsZ ? new THREE.Vector3(0, 0, 1) : new THREE.Vector3(0, 1, 0);
  v.camera.up.copy(up);
  THREE.Object3D.DEFAULT_UP.copy(up);

  for (let i = 0; i < desc.bodyCount; i++) {
    const g = new THREE.Group();
    g.matrixAutoUpdate = true;
    v.root.add(g);
    v.bodies.push(g);
  }
  const box = new THREE.Box3();
  for (const item of desc.items) {
    const mat = new THREE.MeshStandardMaterial({ color: item.color, roughness: item.ground ? 0.95 : 0.55, metalness: item.ground ? 0 : 0.15 });
    const mesh = new THREE.Mesh(geometryFor(item), mat);
    mesh.position.set(item.pos[0], item.pos[1], item.pos[2]);
    mesh.quaternion.set(item.rot[0], item.rot[1], item.rot[2], item.rot[3]);
    mesh.castShadow = !item.ground;
    mesh.receiveShadow = true;
    const parent = item.body >= 0 && item.body < v.bodies.length ? v.bodies[item.body] : v.root;
    parent.add(mesh);
  }

  // Grid on the ground plane.
  if (v.grid) v.scene.remove(v.grid);
  v.grid = new THREE.GridHelper(80, 80, '#3a4660', '#252e40');
  v.grid.material.transparent = true;
  v.grid.material.opacity = 0.55;
  if (desc.upIsZ) v.grid.rotateX(Math.PI / 2);
  v.grid.position.copy(up.clone().multiplyScalar(camera?.groundHeight ?? 0.002));
  v.scene.add(v.grid);

  if (v.lines) { v.scene.remove(v.lines); v.lines = null; }
  if (camera) applyCamera(v, camera);
  void box;
}

function applyCamera(v, cam) {
  const t = new THREE.Vector3(cam.target[0], cam.target[1], cam.target[2]);
  const yaw = cam.yaw * Math.PI / 180, pitch = cam.pitch * Math.PI / 180, d = cam.distance;
  const dir = v.upIsZ
    ? new THREE.Vector3(Math.cos(pitch) * Math.cos(yaw), Math.cos(pitch) * Math.sin(yaw), Math.sin(pitch))
    : new THREE.Vector3(Math.cos(pitch) * Math.sin(yaw), Math.sin(pitch), Math.cos(pitch) * Math.cos(yaw));
  v.camera.position.copy(t.clone().add(dir.multiplyScalar(d)));
  v.controls.target.copy(t);
  v.follow = cam.follow ?? -1;
  placeSun(v, t, d);
  v.controls.update();
}

function placeSun(v, target, distance) {
  const r = Math.max(distance, 6);
  const off = v.upIsZ ? new THREE.Vector3(r * 0.5, r * 0.3, r) : new THREE.Vector3(r * 0.5, r, r * 0.3);
  v.sun.position.copy(target.clone().add(off));
  v.sun.target.position.copy(target);
  const s = v.sun.shadow.camera;
  s.left = -r; s.right = r; s.top = r; s.bottom = -r; s.near = 0.1; s.far = r * 4;
  s.updateProjectionMatrix();
}

// Blazor hands byte[] over as a Uint8Array view whose byteOffset need not be 4-aligned: copy when it is not.
function floats(bytes) {
  return bytes.byteOffset % 4 === 0
    ? new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength >> 2)
    : new Float32Array(bytes.slice().buffer, 0, bytes.byteLength >> 2);
}

export function update(id, frames, lines) {
  const v = views.get(id);
  if (!v) return;
  const f = floats(frames);
  const n = Math.min(v.bodies.length, f.length / 7);
  for (let i = 0; i < n; i++) {
    const k = i * 7, g = v.bodies[i];
    g.position.set(f[k], f[k + 1], f[k + 2]);
    g.quaternion.set(f[k + 3], f[k + 4], f[k + 5], f[k + 6]);
  }
  if (v.follow >= 0 && v.follow < v.bodies.length) {
    const p = v.bodies[v.follow].position;
    const delta = p.clone().sub(v.controls.target);
    v.controls.target.add(delta);
    v.camera.position.add(delta);
    placeSun(v, v.controls.target, v.camera.position.distanceTo(v.controls.target));
  }
  if (lines && lines.byteLength > 0) {
    const pts = floats(lines);
    if (!v.lines || v.lines.geometry.attributes.position.count * 3 !== pts.length) {
      if (v.lines) v.scene.remove(v.lines);
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(pts.length), 3));
      v.lines = new THREE.LineSegments(g, new THREE.LineBasicMaterial({ color: '#F2A33A' }));
      v.scene.add(v.lines);
    }
    v.lines.geometry.attributes.position.array.set(pts);
    v.lines.geometry.attributes.position.needsUpdate = true;
    v.lines.geometry.computeBoundingSphere();
  }
}

export function setCamera(id, camera) {
  const v = views.get(id);
  if (v) applyCamera(v, camera);
}

export function dispose(id) {
  const v = views.get(id);
  if (!v) return;
  v.disposed = true;
  cancelAnimationFrame(v.raf);
  v.resizeObserver.disconnect();
  v.controls.dispose();
  v.renderer.dispose();
  views.delete(id);
}
