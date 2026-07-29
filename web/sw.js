// PWA Service Worker：运行时缓存（首次联网加载后，后续启动秒开/可离线）。
// 注意：Service Worker 仅在 HTTPS 或 localhost 下生效；局域网 http 访问时自动静默跳过。
const CACHE = 'polymatch3-v1';

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(clients.claim()));

self.addEventListener('fetch', (e) => {
  if (e.request.method !== 'GET') return;
  e.respondWith(
    caches.match(e.request).then((hit) => {
      if (hit) return hit;
      return fetch(e.request).then((resp) => {
        if (resp.ok) {
          const copy = resp.clone();
          caches.open(CACHE).then((c) => c.put(e.request, copy));
        }
        return resp;
      });
    })
  );
});
