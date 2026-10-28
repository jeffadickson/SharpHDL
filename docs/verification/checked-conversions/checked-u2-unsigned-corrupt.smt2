(set-logic QF_BV)
(set-option :produce-models true)
(set-option :timeout 5000)
(declare-fun input_0 () (_ BitVec 32))
(assert (let ((a!1 (bvnot (bvnot (ite (bvult #x0000ffff input_0) #b1 #b0)))))
  (not (= (ite (bvult input_0 #x00010000) #b1 #b0) a!1))))

(check-sat)
(get-model)
