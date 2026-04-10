import { Component, type ReactNode } from 'react';

type Props = {
  children: ReactNode;
};

type State = {
  hasError: boolean;
  message?: string;
};

export default class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(error: unknown): State {
    const message = error instanceof Error ? error.message : 'Something went wrong';
    return { hasError: true, message };
  }

  render() {
    if (!this.state.hasError) return this.props.children;

    return (
      <div className="min-h-screen bg-base-100 flex items-center justify-center p-4">
        <div className="card bg-base-200 shadow-xl max-w-xl w-full">
          <div className="card-body">
            <h2 className="card-title">The app crashed</h2>
            <p className="opacity-80 text-sm">{this.state.message}</p>
            <div className="card-actions justify-end">
              <button className="btn btn-primary" type="button" onClick={() => window.location.reload()}>
                Reload
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }
}

