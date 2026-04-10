import Dashboard from './components/Dashboard';
import { Toaster } from 'react-hot-toast';
import ErrorBoundary from './components/ErrorBoundary';

function App() {
  return (
    <ErrorBoundary>
      <Dashboard />
      <Toaster position="top-right" />
    </ErrorBoundary>
  );
}

export default App;
