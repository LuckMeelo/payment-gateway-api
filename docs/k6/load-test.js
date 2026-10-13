import http from 'k6/http';
import { check, sleep } from 'k6';

// Run with: k6 run docs/k6/load-test.js
// Override base URL: BASE_URL=http://localhost:8090 k6 run docs/k6/load-test.js
// Requires the full stack: docker compose up --build

export const options = {
  stages: [
    { duration: '10s', target: 20 }, // ramp up to 20 virtual users
    { duration: '30s', target: 20 }, // hold at 20 VUs
    { duration: '10s', target: 0 },  // ramp down
  ],
  thresholds: {
    // 95% of requests must complete within 500ms
    http_req_duration: ['p(95)<500'],
    // fewer than 1% of requests may fail
    http_req_failed: ['rate<0.01'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8090';

// Card ending 1 is always authorized by the simulator.
// Using a deterministic card avoids random failures from the test itself.
const AUTHORIZED_CARD = '2222405343248871';

const PAYMENT_HEADERS = { 'Content-Type': 'application/json' };

function buildPayload(cardNumber) {
  return JSON.stringify({
    card_number: cardNumber,
    expiry_month: 12,
    expiry_year: 2030,
    currency: 'GBP',
    amount: 100,
    cvv: '123',
  });
}

export default function () {
  // POST - process an authorized payment
  const postRes = http.post(
    `${BASE_URL}/api/payments`,
    buildPayload(AUTHORIZED_CARD),
    { headers: PAYMENT_HEADERS }
  );

  const postOk = check(postRes, {
    'POST status is 201': (r) => r.status === 201,
    'response has id': (r) => {
      try {
        return r.json().id !== undefined;
      } catch {
        return false;
      }
    },
    'status is Authorized': (r) => {
      try {
        return r.json().status === 'Authorized';
      } catch {
        return false;
      }
    },
  });

  // GET - retrieve the stored payment to exercise the full round-trip
  if (postOk && postRes.status === 201) {
    const paymentId = postRes.json().id;

    const getRes = http.get(`${BASE_URL}/api/payments/${paymentId}`);

    check(getRes, {
      'GET status is 200': (r) => r.status === 200,
      'GET returns same id': (r) => {
        try {
          return r.json().id === paymentId;
        } catch {
          return false;
        }
      },
    });
  }

  // Small pause between iterations so VUs do not hammer the gateway in a tight loop.
  // At 20 VUs with 100ms sleep the theoretical max throughput is ~200 req/s.
  sleep(0.1);
}
