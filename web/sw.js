// PWA Service Worker：网络优先 + 缓存兜底（保证更新即时生效，首次联网加载后仍可离线）。
// 注意：Service Worker 仅在 HTTPS 或 localhost 下生效；局域网 http 访问时自动静默跳过。
// 发版时若需强制全量刷新，递增 CACHE 版本号（activate 会清掉旧缓存）。
const CACHE = 'polymatch3-v3';

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(
  caches.keys()
    .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
    .then(() => clients.claim())
));

self.addEventListener('fetch', (e) => {
  if (e.request.method !== 'GET') return;
  e.respondWith(
    fetch(e.request).then((resp) => {
      if (resp.ok) {
        const copy = resp.clone();
        caches.open(CACHE).then((c) => c.put(e.request, copy));
      }
      return resp;
    }).catch(() => caches.match(e.request)) // 断网/服务器不可达时回退缓存
  );
});
