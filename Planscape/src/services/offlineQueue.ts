import AsyncStorage from '@react-native-async-storage/async-storage';
import NetInfo from '@react-native-community/netinfo';
import { QueuedAction } from '../types';

const QUEUE_KEY = 'planscape_offline_queue';
const MAX_ATTEMPTS = 5;
const MAX_QUEUE_SIZE = 200;

type Processor = (action: QueuedAction) => Promise<void>;

export class OfflineQueue {
  private processor: Processor;
  private draining = false;

  constructor(processor: Processor) {
    this.processor = processor;
    this.subscribeNetwork();
  }

  private subscribeNetwork() {
    NetInfo.addEventListener(state => {
      if (state.isConnected && state.isInternetReachable) {
        this.drain().catch((err) => {
          console.warn('[OfflineQueue] Drain failed:', err);
        });
      }
    });
  }

  async enqueue(type: QueuedAction['type'], payload: unknown): Promise<void> {
    const queue = await this.load();
    if (queue.length >= MAX_QUEUE_SIZE) {
      console.warn(`[OfflineQueue] Queue full (${MAX_QUEUE_SIZE}), dropping oldest action: ${queue[0].type}`);
      queue.shift();
    }
    queue.push({
      id: `${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
      type,
      payload,
      createdAt: new Date().toISOString(),
      attempts: 0,
    });
    await this.save(queue);
  }

  async load(): Promise<QueuedAction[]> {
    const raw = await AsyncStorage.getItem(QUEUE_KEY);
    return raw ? JSON.parse(raw) : [];
  }

  private async save(queue: QueuedAction[]): Promise<void> {
    await AsyncStorage.setItem(QUEUE_KEY, JSON.stringify(queue));
  }

  async size(): Promise<number> {
    return (await this.load()).length;
  }

  async drain(): Promise<void> {
    if (this.draining) return;
    this.draining = true;
    try {
      const queue = await this.load();
      const remaining: QueuedAction[] = [];
      for (const action of queue) {
        try {
          await this.processor(action);
        } catch (err) {
          action.attempts += 1;
          action.lastError = err instanceof Error ? err.message : String(err);
          if (action.attempts < MAX_ATTEMPTS) {
            remaining.push(action);
            await this.sleep(Math.min(30000, 500 * 2 ** action.attempts));
          }
        }
      }
      await this.save(remaining);
    } finally {
      this.draining = false;
    }
  }

  private sleep(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}
