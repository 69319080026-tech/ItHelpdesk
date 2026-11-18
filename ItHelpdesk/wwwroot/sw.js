// Service worker: เก็บหน้าแอปไว้ในเครื่อง ส่วนข้อมูล API ดึงจากเซิร์ฟเวอร์เสมอ
const CACHE = 'helpdesk-v1';
const SHELL = ['/', '/index.html', '/manifest.json', '/icon.svg'];

self.addEventListener('install', e => e.waitUntil(caches.open(CACHE).then(c => c.addAll(SHELL))));
self.addEventListener('activate', e => e.waitUntil(
  caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k))))));

self.addEventListener('fetch', e => {
  const url = new URL(e.request.url);
  if (e.request.method !== 'GET' || !SHELL.includes(url.pathname)) return;   // API ไม่ cache
  e.respondWith(fetch(e.request).catch(() => caches.match(e.request)));
});
