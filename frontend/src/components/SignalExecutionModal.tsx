import { useMemo, useState } from 'react';
import toast from 'react-hot-toast';
import { useExecuteSignal } from '../hooks/useSignals';
import type { TradingSignal } from '../types/api';

type Props = {
  signal: TradingSignal;
  onClose: () => void;
};

export default function SignalExecutionModal({ signal, onClose }: Props) {
  const execute = useExecuteSignal();

  const [entryPrice, setEntryPrice] = useState<number>(() => Number(signal.entryPrice));
  const [targetPrice, setTargetPrice] = useState<number>(() => Number(signal.targetPrice));
  const [stopLoss, setStopLoss] = useState<number>(() => Number(signal.stopLoss));

  const isValid = useMemo(() => entryPrice > 0 && targetPrice > 0 && stopLoss > 0, [entryPrice, targetPrice, stopLoss]);

  const onConfirm = async () => {
    if (!isValid) return;
    try {
      await execute.mutateAsync({
        signalId: signal.id,
        payload: { entryPrice, targetPrice, stopLoss },
      });
      toast.success(`Executed ${signal.symbol}`);
      onClose();
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'Failed to execute signal');
    }
  };

  return (
    <div className="modal modal-open">
      <div className="modal-box">
        <h3 className="font-bold text-lg">Execute signal</h3>
        <p className="opacity-70 text-sm mt-1">{signal.symbol}</p>

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mt-4">
          <label className="form-control">
            <div className="label">
              <span className="label-text">Entry</span>
            </div>
            <input
              className="input input-bordered input-sm"
              type="number"
              inputMode="decimal"
              value={Number.isFinite(entryPrice) ? entryPrice : ''}
              onChange={(e) => setEntryPrice(Number(e.target.value))}
            />
          </label>

          <label className="form-control">
            <div className="label">
              <span className="label-text">Target</span>
            </div>
            <input
              className="input input-bordered input-sm"
              type="number"
              inputMode="decimal"
              value={Number.isFinite(targetPrice) ? targetPrice : ''}
              onChange={(e) => setTargetPrice(Number(e.target.value))}
            />
          </label>

          <label className="form-control">
            <div className="label">
              <span className="label-text">Stop-loss</span>
            </div>
            <input
              className="input input-bordered input-sm"
              type="number"
              inputMode="decimal"
              value={Number.isFinite(stopLoss) ? stopLoss : ''}
              onChange={(e) => setStopLoss(Number(e.target.value))}
            />
          </label>
        </div>

        <div className="modal-action">
          <button className="btn btn-ghost" type="button" onClick={onClose} disabled={execute.isPending}>
            Cancel
          </button>
          <button
            className="btn btn-success"
            type="button"
            onClick={onConfirm}
            disabled={!isValid || execute.isPending}
          >
            Confirm
          </button>
        </div>
      </div>
      <button className="modal-backdrop" type="button" onClick={onClose} aria-label="Close modal" />
    </div>
  );
}

